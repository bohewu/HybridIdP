using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public sealed partial class NativeFieldPolicyService
{
    private static readonly string[] SourceProperties = ["displayName", "givenName", "surname", "department", "title", "employeeId"];
    private static readonly JsonSerializerOptions MappingJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task<NativeFieldMappingsDto> GetMappingsAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadMappingDocumentAsync(cancellationToken);
        var policies = await db.NativeFieldSourcePolicies.AsNoTracking().OrderBy(x => x.Source).ToListAsync(cancellationToken);
        var configured = new List<(NativeFieldSource Source, string Namespace)>();
        if (!string.IsNullOrWhiteSpace(proofOptions.Value.TrustedProviderNamespace))
            configured.Add((NativeFieldSource.Proof, proofOptions.Value.TrustedProviderNamespace));
        configured.AddRange(options.Value.TrustedStage1ProviderNamespaces.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).Select(x => (NativeFieldSource.Stage1, x)));
        var neutralSources = neutralQueries is null ? [] : (await neutralQueries().GetConfigurationAsync(cancellationToken)).Sources
            .Where(x => x.Capability == "native-profile").ToArray();
        configured.AddRange(neutralSources.Select(x => (NativeFieldSource.NeutralQuery, x.ProviderNamespace)).Distinct());
        var mappings = document?.Mappings ?? configured.SelectMany(source => Fields.Select(field =>
            new NativeFieldMappingDto(field, source.Source, source.Namespace, SourceProperties[(int)field]))).ToArray();
        var proofPolicy = policies.SingleOrDefault(x => x.Source == "Proof");
        var fallback = document?.FirstNameFallback ?? new NativeFieldMappingFallbackDto(
            proofOptions.Value.TrustedProviderNamespace,
            proofPolicy?.FirstNameDisplayNameFallback == true &&
            proofPolicy.FirstNameFallbackProviderNamespace == proofOptions.Value.TrustedProviderNamespace);
        fallback = NormalizeDisabledFallback(fallback);
        var userNamePolicies = document?.UserNamePolicies ?? [];
        var sources = configured.Concat(mappings.Select(x => (x.Source, x.ProviderNamespace)))
            .Concat(userNamePolicies.Select(x => (x.Source, x.ProviderNamespace))).Distinct()
            .Select(x => new NativeFieldMappingSourceDto(x.Source, x.Item2,
                x.Source == NativeFieldSource.NeutralQuery
                    ? neutralSources.Any(source => source.ProviderNamespace == x.Item2 && source.Available)
                    : IsEnabled(x.Source) && IsTrustedNamespace(x.Source, x.Item2),
                policies.SingleOrDefault(p => p.Source == x.Source.ToString())?.Paused == true,
                x.Source == NativeFieldSource.Proof && proofOptions.Value.Enabled && IsTrustedNamespace(x.Source, x.Item2)))
            .OrderBy(x => x.Source).ThenBy(x => x.ProviderNamespace, StringComparer.Ordinal).ToArray();
        // Include legacy fallback, pause and deployment trust in the revision. Reads never create rows.
        var revision = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { document, policies, sources, fallback }))).AsSpan(0, 16));
        return new(revision, mappings, fallback, sources, SourceProperties, userNamePolicies);
    }

    public async Task<NativeFieldMappingsDto> SetMappingsAsync(NativeFieldMappingsRequest request,
        CancellationToken cancellationToken = default)
    {
        request = request with { FirstNameFallback = NormalizeDisabledFallback(request.FirstNameFallback) };
        ValidateMappingShape(request.Mappings, request.FirstNameFallback, "invalid_mapping");
        // Configuration and the native writers share source-policy concurrency rows. The transaction
        // also protects the absent Settings row and atomically saves the complete draft plus fallback.
        if (db is not DbContext context || !context.Database.IsRelational())
            throw new NativeFieldPolicyException("transaction_unavailable");
        Require(!context.ChangeTracker.HasChanges(), "conflict");
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var committed = false;
        try
        {
            var current = await GetMappingsAsync(cancellationToken);
            Require(request.ExpectedRevision == current.Revision, "conflict");
            // Older clients cannot silently erase an administrator's naming choice.
            var userNamePolicies = request.UserNamePolicies ?? current.UserNamePolicies ?? [];
            ValidateUserNamePolicies(userNamePolicies, "invalid_mapping");
            foreach (var policy in userNamePolicies)
            {
                if (current.UserNamePolicies?.Contains(policy) == true) continue;
                Require(current.Sources.Any(x => x.Source == policy.Source && x.ProviderNamespace == policy.ProviderNamespace &&
                    x.UserNamePolicyAvailable), "source_unavailable");
            }
            foreach (var mapping in request.Mappings)
            {
                // An unavailable saved row may be retained or removed, but never newly assigned.
                if (current.Mappings.Contains(mapping)) continue;
                Require(current.Sources.Any(x => x.Source == mapping.Source && x.ProviderNamespace == mapping.ProviderNamespace &&
                    x.Enabled && !x.Paused), "source_unavailable");
            }
            if (request.FirstNameFallback.Enabled && request.FirstNameFallback != current.FirstNameFallback)
                Require(current.Sources.Any(x => (x.Source is NativeFieldSource.Proof or NativeFieldSource.NeutralQuery) &&
                    x.ProviderNamespace == request.FirstNameFallback.ProviderNamespace && x.Enabled && !x.Paused), "source_unavailable");
            var setting = await db.Settings.SingleOrDefaultAsync(x => x.Key == SettingKeys.NativeFieldMappings, cancellationToken);
            if (setting is null)
            {
                setting = new Setting { Id = Guid.NewGuid(), Key = SettingKeys.NativeFieldMappings };
                db.Settings.Add(setting);
            }
            setting.Value = JsonSerializer.Serialize(new MappingDocument
            {
                Revision = Guid.NewGuid(), Mappings = request.Mappings.ToArray(), FirstNameFallback = request.FirstNameFallback,
                UserNamePolicies = userNamePolicies.ToArray()
            }, MappingJson);
            setting.UpdatedUtc = DateTime.UtcNow;
            foreach (var source in Enum.GetValues<NativeFieldSource>().Where(source => source != NativeFieldSource.NeutralQuery ||
                         current.Sources.Any(x => x.Source == NativeFieldSource.NeutralQuery)))
            {
                var policy = await db.NativeFieldSourcePolicies.SingleOrDefaultAsync(x => x.Source == source.ToString(), cancellationToken);
                if (policy is null)
                {
                    policy = new NativeFieldSourcePolicy { Source = source.ToString() };
                    db.NativeFieldSourcePolicies.Add(policy);
                }
                policy.Revision = Guid.NewGuid();
            }
            await db.SaveChangesAsync(cancellationToken);
            var result = await GetMappingsAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return result;
        }
        catch (DbUpdateException) { throw new NativeFieldPolicyException("conflict"); }
        catch (DbException) { throw new NativeFieldPolicyException("conflict"); }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                finally { context.ChangeTracker.Clear(); }
            }
        }
    }

    private async Task<MappingDocument?> ReadMappingDocumentAsync(CancellationToken ct)
    {
        var setting = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Key == SettingKeys.NativeFieldMappings, ct);
        if (setting is null) return null;
        try
        {
            if (string.IsNullOrWhiteSpace(setting.Value) || setting.Value.Length > 65536) throw new JsonException();
            using var parsed = JsonDocument.Parse(setting.Value, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateMembers(parsed.RootElement);
            var document = JsonSerializer.Deserialize<MappingDocument>(setting.Value, MappingJson);
            if (document is null || document.Revision == Guid.Empty) throw new JsonException();
            ValidateMappingShape(document.Mappings, document.FirstNameFallback, "invalid_stored_state");
            ValidateUserNamePolicies(document.UserNamePolicies, "invalid_stored_state");
            return document;
        }
        catch (JsonException) { throw new NativeFieldPolicyException("invalid_stored_state"); }
    }

    private static NativeFieldMappingFallbackDto NormalizeDisabledFallback(NativeFieldMappingFallbackDto fallback) =>
        fallback is { Enabled: false } && string.IsNullOrWhiteSpace(fallback.ProviderNamespace)
            ? fallback with { ProviderNamespace = null }
            : fallback;

    private static void ValidateMappingShape(IReadOnlyList<NativeFieldMappingDto>? mappings,
        NativeFieldMappingFallbackDto? fallback, string code)
    {
        Require(mappings is not null && mappings.Count <= 128 && fallback is not null, code);
        var keys = new HashSet<(NativeField, NativeFieldSource, string)>();
        foreach (var mapping in mappings!)
        {
            Require(mapping is not null && Enum.IsDefined(mapping.Field) && Enum.IsDefined(mapping.Source) &&
                ValidNamespace(mapping.ProviderNamespace) && SourceProperties.Contains(mapping.SourceProperty, StringComparer.Ordinal), code);
            Require(keys.Add((mapping!.Field, mapping.Source, mapping.ProviderNamespace)), code);
        }
        Require(fallback!.ProviderNamespace is null || ValidNamespace(fallback.ProviderNamespace), code);
        Require(!fallback.Enabled || ValidNamespace(fallback.ProviderNamespace), code);
    }

    private static (IReadOnlyDictionary<NativeField, string?> Values, bool UsedFirstNameFallback) MapNativeSourceValues(
        NativeFieldSourceIdentity identity, IReadOnlyDictionary<NativeField, string?> originalValues,
        MappingDocument? document, NativeFieldMappingFallbackDto? fallback)
    {
        IReadOnlyDictionary<NativeField, string?> values = document is null ? originalValues : document.Mappings
            .Where(x => x.Source == identity.Source && x.ProviderNamespace == identity.ProviderNamespace)
            .ToDictionary(x => x.Field, x => originalValues.GetValueOrDefault(PropertyField(x.SourceProperty)));
        // Inputs are fresh assured fields from this source, before any output remapping.
        // Never use stored native values, Profile extras or another source's display name.
        if ((identity.Source is NativeFieldSource.Proof or NativeFieldSource.NeutralQuery) &&
            fallback is { Enabled: true } && fallback.ProviderNamespace == identity.ProviderNamespace &&
            (document is null || document.Mappings.Any(x => x.Field == NativeField.FirstName &&
                x.Source == identity.Source && x.ProviderNamespace == identity.ProviderNamespace && x.SourceProperty == "givenName")) &&
            string.IsNullOrWhiteSpace(originalValues.GetValueOrDefault(NativeField.FirstName)) &&
            originalValues.GetValueOrDefault(NativeField.Nickname) is { Length: <= 100 } displayName &&
            !string.IsNullOrWhiteSpace(displayName))
            return (new Dictionary<NativeField, string?>(values) { [NativeField.FirstName] = displayName }, true);
        return (values, false);
    }

    private bool IsTrustedNamespace(NativeFieldSource source, string providerNamespace) => source switch
    {
        NativeFieldSource.Proof => providerNamespace == proofOptions.Value.TrustedProviderNamespace,
        NativeFieldSource.Stage1 => options.Value.TrustedStage1ProviderNamespaces.Contains(providerNamespace, StringComparer.Ordinal),
        _ => false
    };

    private static bool ValidNamespace(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 &&
        value == value.Trim() && !value.Any(char.IsControl);

    private static void ValidateUserNamePolicies(IReadOnlyList<ProofUserNamePolicyDto>? policies, string code)
    {
        Require(policies is not null && policies.Count <= 128, code);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var policy in policies!)
        {
            Require(policy is not null && policy.Source == NativeFieldSource.Proof && ValidNamespace(policy.ProviderNamespace) &&
                Enum.IsDefined(policy.Strategy), code);
            Require(namespaces.Add(policy!.ProviderNamespace), code);
        }
    }

    public async Task<ProofUserNameStrategy> GetProofUserNameStrategyAsync(string providerNamespace, CancellationToken cancellationToken = default)
    {
        // Read the guard before the document: a concurrent save must invalidate this decision.
        var policy = await db.NativeFieldSourcePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Source == "Proof", cancellationToken);
        var setting = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Key == SettingKeys.NativeFieldMappings, cancellationToken);
        ProofUserNamePolicyDto[] policies = [];
        if (setting is not null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(setting.Value) || setting.Value.Length > 65536) throw new JsonException();
                using var parsed = JsonDocument.Parse(setting.Value, new JsonDocumentOptions { MaxDepth = 8 });
                if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                RejectDuplicateMembers(parsed.RootElement);
                // Legacy native-policy corruption must not turn optional native writes into a login gate.
                if (parsed.RootElement.TryGetProperty("UserNamePolicies", out var element))
                    policies = element.Deserialize<ProofUserNamePolicyDto[]>(MappingJson)!;
                ValidateUserNamePolicies(policies, "invalid_stored_state");
            }
            catch (JsonException) { throw new NativeFieldPolicyException("invalid_stored_state"); }
        }
        var selection = policies.SingleOrDefault(x => x.ProviderNamespace == providerNamespace);
        if (selection?.Strategy == ProofUserNameStrategy.Email)
            Require(proofOptions.Value.Enabled && IsTrustedNamespace(NativeFieldSource.Proof, providerNamespace), "source_unavailable");
        // Native synchronization pause does not change credential proof or new-account naming.
        if (proofOptions.Value.Enabled && IsTrustedNamespace(NativeFieldSource.Proof, providerNamespace))
            StageMappingGuard(NativeFieldSource.Proof, policy);
        return selection?.Strategy ?? ProofUserNameStrategy.CanonicalAccount;
    }

    private static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicateMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateMembers(item);
    }

    private static NativeField PropertyField(string property) => property switch
    {
        "displayName" => NativeField.Nickname, "givenName" => NativeField.FirstName, "surname" => NativeField.LastName,
        "department" => NativeField.Department, "title" => NativeField.JobTitle, "employeeId" => NativeField.EmployeeId,
        _ => throw new NativeFieldPolicyException("invalid_stored_state")
    };

    private void StageMappingGuard(NativeFieldSource source, NativeFieldSourcePolicy? snapshot)
    {
        if (db is not DbContext context) throw new NativeFieldPolicyException("transaction_unavailable");
        // Identity's UserStore attaches the user before updating it. Do not eagerly detect its
        // pending native mutations here: Attach would accept those as concurrency originals.
        var autoDetect = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var tracked = context.ChangeTracker.Entries<NativeFieldSourcePolicy>().SingleOrDefault(x => x.Entity.Source == source.ToString());
            if (snapshot is null)
            {
                if (tracked is null) db.NativeFieldSourcePolicies.Add(new NativeFieldSourcePolicy { Source = source.ToString() });
                else Require(tracked.State == EntityState.Added && tracked.Entity.Revision == Guid.Empty, "conflict");
                return;
            }
            if (tracked is null) tracked = context.Attach(snapshot);
            Require(tracked.Entity.Revision == snapshot.Revision && tracked.State != EntityState.Added, "conflict");
            // Same-value update acquires the existing row lock and checks the original revision in the
            // caller's SaveChanges transaction, without turning a login into a settings revision change.
            tracked.Property(x => x.Revision).OriginalValue = snapshot.Revision;
            tracked.Property(x => x.Revision).IsModified = true;
        }
        finally { context.ChangeTracker.AutoDetectChangesEnabled = autoDetect; }
    }

    private sealed class MappingDocument
    {
        [JsonRequired] public Guid Revision { get; set; }
        [JsonRequired] public NativeFieldMappingDto[] Mappings { get; set; } = [];
        [JsonRequired] public NativeFieldMappingFallbackDto FirstNameFallback { get; set; } = new(null, false);
        public ProofUserNamePolicyDto[] UserNamePolicies { get; set; } = [];
    }
}
