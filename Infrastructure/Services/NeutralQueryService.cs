using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Application.Utilities;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure.Options;
using Infrastructure.Directory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class NeutralQueryService(
    IApplicationDbContext db, IDataProtectionProvider protection,
    IOptionsMonitor<ProviderNativeProfileOptions> native, IOptionsMonitor<ProviderProfileOptions> profile,
    IOptionsMonitor<ProviderMetadataRefreshOptions> metadata, IOptionsMonitor<ProviderLifecycleOptions> lifecycle,
    IOptionsMonitor<RecoveryIdentityVerificationOptions> recovery, IOptionsMonitor<LegacyPasswordSyncOptions> password,
    INativeFieldPolicyService nativeFields,
    IOptionsMonitor<DirectoryNativeProfileOptions>? directoryNative = null,
    IOptions<DirectoryIntegrationOptions>? directory = null,
    IOptions<DirectoryLookupOptions>? directoryLookup = null,
    IOptions<DirectoryCredentialTransportOptions>? directoryConnection = null) : INeutralQueryService
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private readonly IDataProtector _protector = protection.CreateProtector("NeutralQuery.Approval.v2");

    public async Task<QueryConfigurationStateDto> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync<ConfigurationDocument>(SettingKeys.NeutralQueryConfiguration, cancellationToken) ?? new(Guid.Empty, []);
        ValidateShape(document.Configurations);
        var deployed = Sources();
        var sources = deployed.Select(x => Describe(x.Key.Capability, x.Key.SourceId, x.Value)).ToList();
        foreach (var saved in document.Configurations)
            if (!sources.Any(x => x.Capability == saved.Capability && x.SourceId == saved.SourceId && x.ProviderNamespace == saved.ProviderNamespace))
                sources.Add(new(saved.Capability, saved.SourceId, saved.ProviderNamespace, false, "source_unavailable", [], [], saved.QueryRule));
        return new(Revision(new { document, deployed = deployed.OrderBy(x => x.Key.Capability).ThenBy(x => x.Key.SourceId).ToArray() }),
            document.Configurations, sources);
    }

    public Task<QueryConfigurationStateDto> SetConfigurationAsync(QueryConfigurationRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(async () =>
        {
            ValidateShape(request.Configurations);
            var current = await GetConfigurationAsync(cancellationToken);
            Require(current.Revision == request.ExpectedRevision, "conflict");
            var deployed = Sources();
            foreach (var row in request.Configurations)
            {
                // A blocked saved selection remains visible and can be removed, never silently replaced.
                if (current.Configurations.Any(x => Serialize(x) == Serialize(row))) continue;
                if (row.WireMode == NeutralQueryContract.Legacy)
                {
                    Require(!deployed.TryGetValue((row.Capability, row.SourceId), out var legacySource) ||
                        legacySource.Directory is null, "invalid_mapping");
                    continue;
                }
                Require(deployed.TryGetValue((row.Capability, row.SourceId), out var source) && Usable(row.Capability, source), "source_unavailable");
                Require(Supports(row, source!), "invalid_mapping");
            }
            await WriteAsync(SettingKeys.NeutralQueryConfiguration, new ConfigurationDocument(Guid.NewGuid(), request.Configurations.ToArray()), cancellationToken);
            return await GetConfigurationAsync(cancellationToken);
        }, cancellationToken);

    public async Task<QueryApprovalStateDto> GetApprovalAsync(string capability, string sourceId, Guid userId, CancellationToken cancellationToken = default)
    {
        var material = await ApprovalMaterialAsync(capability, sourceId, userId, cancellationToken);
        var approval = await ReadApprovalAsync(capability, sourceId, userId, cancellationToken);
        var approved = approval is { Approved: true } && approval.ValueFingerprint == material.Fingerprint;
        return new(Revision(new { material.Fingerprint, approval }), approved,
            material.Mapping.Mappings.Select(x => x.ValueSource).ToArray(), material.Mapping.Mappings.Any(x => x.Attribute == "nationalIdHash"));
    }

    public Task<QueryApprovalStateDto> SetApprovalAsync(string capability, string sourceId, Guid userId,
        QueryApprovalRequest request, CancellationToken cancellationToken = default) => MutateAsync(async () =>
    {
        var current = await GetApprovalAsync(capability, sourceId, userId, cancellationToken);
        Require(current.Revision == request.ExpectedRevision, "conflict");
        var material = await ApprovalMaterialAsync(capability, sourceId, userId, cancellationToken);
        Require(!request.Approved || !current.RequiresNationalIdAlgorithmApproval || request.NationalIdAlgorithmApproved, "algorithm_approval_required");
        Require(!request.Approved || material.Complete, "missing_input");
        var approval = new ApprovalDocument(Guid.NewGuid(), request.Approved, material.Fingerprint);
        await WriteAsync(ApprovalKey(capability, sourceId, userId), _protector.Protect(Serialize(approval)), cancellationToken);
        return await GetApprovalAsync(capability, sourceId, userId, cancellationToken);
    }, cancellationToken);

    public async Task<NeutralQueryResolution> ResolveAsync(string capability, Guid userId, string? sourceId = null,
        IReadOnlyList<TrustedRuntimeQueryValue>? runtimeValues = null, CancellationToken cancellationToken = default)
    {
        try
        {
            Require(NeutralQueryContract.Capabilities.Contains(capability), "invalid_capability");
            var state = await GetConfigurationAsync(cancellationToken);
            var mappings = state.Configurations.Where(x => x.Capability == capability && (sourceId is null || x.SourceId == sourceId)).ToArray();
            if (mappings.Length == 0) return new("Legacy");
            if (mappings.Length != 1) return new("Unavailable");
            var mapping = mappings[0];
            if (mapping.WireMode == NeutralQueryContract.Legacy)
                return new(Sources().TryGetValue((capability, mapping.SourceId), out var legacySource) &&
                    legacySource.Directory is not null ? "Unavailable" : "Legacy");
            if (!Sources().TryGetValue((capability, mapping.SourceId), out var deployed) || !Usable(capability, deployed) || !Supports(mapping, deployed)) return new("Unavailable");
            var source = deployed.Policy;
            var (user, person) = await AccountAsync(userId, cancellationToken);
            var supplied = runtimeValues?.ToArray() ?? [];
            var material = await ApprovalMaterialAsync(capability, mapping.SourceId, userId, cancellationToken);
            var approval = await ReadApprovalAsync(capability, mapping.SourceId, userId, cancellationToken);
            var approved = approval is { Approved: true } && approval.ValueFingerprint == material.Fingerprint;
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in mapping.Mappings)
            {
                string? raw;
                if (item.ValueSource.StartsWith("runtime.", StringComparison.Ordinal))
                {
                    var candidates = supplied.Where(x => x.ValueSource == item.ValueSource).ToArray();
                    if (candidates.Length != 1) return new("MissingInput");
                    var evidence = candidates[0];
                    if (!evidence.Assured || evidence.LocalAccountId != userId || evidence.SecurityStamp != user.SecurityStamp ||
                        evidence.ValidatedAt > DateTimeOffset.UtcNow || evidence.ValidatedAt < DateTimeOffset.UtcNow.AddMinutes(-5) ||
                        !source.TrustedValueProviderNamespaces.Contains(evidence.ProviderNamespace, StringComparer.Ordinal)) return new("MissingInput");
                    raw = evidence.Value;
                }
                else
                {
                    if (!approved && !await HasNativeProvenanceAsync(user, person, item, source, cancellationToken)) return new("MissingInput");
                    raw = StoredValue(user, person, item.ValueSource);
                }
                if (!NeutralQueryContract.TryNormalize(item.Attribute, raw, out var normalized)) return new("MissingInput");
                values.Add(item.Attribute, normalized);
            }
            // Include original stored material, source policy (including endpoint/secret), approval revision,
            // account/security state and mapping revision. Request IDs are independent correlation tokens.
            var fingerprint = Hash(new { state.Revision, mapping, deployed, material.Fingerprint, approval, values, supplied,
                user.SecurityStamp, user.ConcurrencyStamp, user.IsActive, user.PersonId,
                person?.Status, person?.StartDate, person?.EndDate, person?.IsDeleted, user.NativeFieldStateJson,
                PersonNativeState = person?.NativeFieldStateJson });
            var request = new NeutralQueryRequest { ContractType = capability, ProviderNamespace = mapping.ProviderNamespace,
                Query = new(new ReadOnlyDictionary<string, string>(values), mapping.QueryRule) };
            return new("Ready", new(userId, user.PersonId, mapping.SourceId, request, fingerprint, state.Revision,
                deployed.Directory is null ? new(new Uri(source.Endpoint), source.SharedSecret, source.Timeout) : null,
                Array.AsReadOnly(supplied), deployed.Directory));
        }
        catch (NeutralQueryException) { return new("Unavailable"); }
    }

    public async Task<bool> IsCurrentAsync(NeutralQueryContext context, CancellationToken cancellationToken = default)
    {
        var current = await ResolveAsync(context.Request.ContractType, context.LocalAccountId, context.SourceId, context.RuntimeValues, cancellationToken);
        return current.Status == "Ready" && current.Context!.Fingerprint == context.Fingerprint &&
            current.Context.PersonId == context.PersonId && current.Context.Request.ProviderNamespace == context.Request.ProviderNamespace &&
            Serialize(current.Context.Request.Query) == Serialize(context.Request.Query);
    }

    private Dictionary<(string Capability, string SourceId), DeployedSource> Sources()
    {
        var result = new Dictionary<(string, string), DeployedSource>();
        void Add(string capability, bool enabled, Dictionary<string, NeutralQuerySourceOptions> sources)
        {
            foreach (var (id, source) in sources)
            {
                // Snapshot monitors so a pending context never shares a mutable deployment object.
                var copy = JsonSerializer.Deserialize<NeutralQuerySourceOptions>(Serialize(source))!;
                copy.Enabled &= enabled;
                result.Add((capability, id), new(copy));
            }
        }
        Add("native-profile", native.CurrentValue.Enabled, native.CurrentValue.QuerySources);
        Add("profile", profile.CurrentValue.Enabled, profile.CurrentValue.QuerySources);
        Add("metadata", metadata.CurrentValue.Enabled, metadata.CurrentValue.QuerySources);
        Add("lifecycle-status", lifecycle.CurrentValue.Enabled, lifecycle.CurrentValue.QuerySources);
        Add("recovery-identity-verification", recovery.CurrentValue.Enabled, recovery.CurrentValue.QuerySources);
        Add("password-sync", password.CurrentValue.Enabled, password.CurrentValue.QuerySources);
        if (directoryNative?.CurrentValue is { } direct && !string.IsNullOrWhiteSpace(direct.SourceId))
        {
            var key = ("native-profile", direct.SourceId);
            var policy = new NeutralQuerySourceOptions
            {
                Enabled = direct.Enabled, Trusted = direct.Trusted, Paused = direct.Paused,
                ProviderNamespace = direct.ProviderNamespace, PolicyRevision = direct.PolicyRevision,
                AcceptedAttributeSets = [[direct.SamAccountNameQueryAttribute ?? string.Empty]],
                AllowedValueSources = new() { [direct.SamAccountNameQueryAttribute ?? string.Empty] = direct.AllowedValueSources?.ToArray()! }
            };
            var valid = ProviderProfileContract.IsValidKey(direct.SourceId) &&
                direct.SamAccountNameQueryAttribute is "accountName" or "employeeId" &&
                directory?.Value is { Enabled: true, Transport: DirectoryTransport.Ldaps or DirectoryTransport.StartTls or DirectoryTransport.WindowsNegotiate } &&
                directoryLookup?.Value.Timeout is { } timeout && timeout > TimeSpan.Zero && timeout <= TimeSpan.FromSeconds(30) &&
                directoryConnection?.Value.IsConfigured == true;
            // A duplicate local source key is ambiguous, never a transport override or HTTP fallback.
            if (result.ContainsKey(key)) valid = false;
            result[key] = new(policy, new(direct.SamAccountNameQueryAttribute ?? string.Empty),
                Hash(new { direct, Integration = directory?.Value, Lookup = directoryLookup?.Value, Connection = directoryConnection?.Value }), valid);
        }
        return result;
    }

    private static bool Usable(string capability, DeployedSource deployed) =>
        (deployed.Directory is null || capability == "native-profile" && deployed.DirectoryConfigurationValid) &&
        UsablePolicy(capability, deployed.Policy, deployed.Directory is not null);

    private static bool UsablePolicy(string capability, NeutralQuerySourceOptions source, bool direct) => source.Enabled && source.Trusted && !source.Paused &&
        NeutralQueryContract.IsText(source.ProviderNamespace, 200) && NeutralQueryContract.IsText(source.PolicyRevision, 128) &&
        NeutralQueryContract.IsRule(source.QueryRule) && source.Timeout > TimeSpan.Zero && source.Timeout <= TimeSpan.FromSeconds(
            capability is "lifecycle-status" or "recovery-identity-verification" ? 10 : 30) &&
        (direct || (Uri.TryCreate(source.Endpoint, UriKind.Absolute, out var endpoint) && !string.IsNullOrEmpty(endpoint.Host) &&
        string.IsNullOrEmpty(endpoint.UserInfo) && string.IsNullOrEmpty(endpoint.Query) && string.IsNullOrEmpty(endpoint.Fragment) &&
        (endpoint.Scheme == "https" || source.AllowPrivateNetworkHttp && capability is not ("lifecycle-status" or "recovery-identity-verification") && endpoint.Scheme == "http") &&
        source.SharedSecret is { Length: > 0 } && source.SharedSecret.All(c => c is >= '!' and <= '~'))) &&
        source.AcceptedAttributeSets is { Length: > 0 and <= 7 } && source.AcceptedAttributeSets.All(set =>
            set is { Length: > 0 and <= 3 } && set.Distinct(StringComparer.Ordinal).Count() == set.Length && set.All(NeutralQueryContract.ValueSources.ContainsKey)) &&
        source.AllowedValueSources is not null && source.AllowedValueSources.All(pair => NeutralQueryContract.ValueSources.TryGetValue(pair.Key, out var valid) &&
            pair.Value is { Length: > 0 } && pair.Value.All(value => valid.Contains(value, StringComparer.Ordinal))) && source.TrustedValueProviderNamespaces is not null;

    private static bool Supports(QueryConfigurationDto row, DeployedSource deployed) => SupportsPolicy(row, deployed.Policy);

    private static bool SupportsPolicy(QueryConfigurationDto row, NeutralQuerySourceOptions source) => row.ProviderNamespace == source.ProviderNamespace &&
        row.QueryRule == source.QueryRule && source.AcceptedAttributeSets.Any(set => set.Order(StringComparer.Ordinal).SequenceEqual(
            row.Mappings.Select(x => x.Attribute).Order(StringComparer.Ordinal))) && row.Mappings.All(x =>
            IsSupportedValueSource(x.ValueSource) && source.AllowedValueSources.TryGetValue(x.Attribute, out var sources) &&
            sources.Contains(x.ValueSource, StringComparer.Ordinal));

    // The wire vocabulary reserves runtime sources; current product callers supply persisted fields only.
    private static bool IsSupportedValueSource(string valueSource) => valueSource is
        "person.employeeId" or "user.employeeId" or "person.nationalId" or "user.userName";

    private static QuerySourceDto Describe(string capability, string sourceId, DeployedSource deployed)
    {
        var source = deployed.Policy;
        var available = Usable(capability, deployed);
        var candidates = available ? source.AllowedValueSources
            .Select(x => new QueryCandidateDto(x.Key, x.Key == "nationalIdHash" ? "Sha256Hex" : "String",
                x.Value.Where(IsSupportedValueSource).ToArray()))
            .Where(x => x.ValueSources.Count > 0).ToArray() : [];
        var sets = available ? source.AcceptedAttributeSets
            .Where(set => set.All(attribute => candidates.Any(candidate => candidate.Attribute == attribute))).ToArray() : [];
        var unavailableReason = !source.Enabled ? "deployment_disabled" :
            !source.Trusted ? "source_untrusted" : source.Paused ? "source_paused" :
            !available ? "invalid_configuration" : sets.Length == 0 ? "unsupported_inputs" : null;
        available &= sets.Length > 0;
        return new(capability, sourceId, source.ProviderNamespace, available, unavailableReason,
            available ? candidates.Where(x => sets.Any(set => set.Contains(x.Attribute))).ToArray() : [],
            available ? sets : [], source.QueryRule, deployed.Directory is null ? "http" : "directory");
    }

    private static void ValidateShape(IReadOnlyList<QueryConfigurationDto>? rows)
    {
        Require(rows is not null && rows.Count <= 48, "invalid_mapping");
        var keys = new HashSet<(string, string)>();
        foreach (var row in rows!)
        {
            Require(row is not null && NeutralQueryContract.Capabilities.Contains(row.Capability) &&
                ProviderProfileContract.IsValidKey(row.SourceId) && keys.Add((row.Capability, row.SourceId)) &&
                NeutralQueryContract.IsText(row.ProviderNamespace, 200) && NeutralQueryContract.IsRule(row.QueryRule) &&
                row.WireMode is NeutralQueryContract.Legacy or NeutralQueryContract.WireMode && row.Mappings is not null, "invalid_mapping");
            Require(row!.Mappings.Count <= 3 && (row.WireMode == NeutralQueryContract.Legacy || row.Mappings.Count > 0) &&
                row.Mappings.All(x => x is not null && NeutralQueryContract.ValueSources.TryGetValue(x.Attribute, out var allowed) &&
                    allowed.Contains(x.ValueSource, StringComparer.Ordinal)) && row.Mappings.Select(x => x.Attribute).Distinct().Count() == row.Mappings.Count, "invalid_mapping");
        }
    }

    private async Task<(ApplicationUser User, Person? Person)> AccountAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        Require(user is not null, "not_found");
        var person = user!.PersonId is { } id ? await db.Persons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        Require(user.PersonId is null || person is not null, "conflict");
        return (user, person);
    }
    private async Task<ApprovalMaterial> ApprovalMaterialAsync(string capability, string sourceId, Guid userId, CancellationToken ct)
    {
        var state = await GetConfigurationAsync(ct);
        var mapping = state.Configurations.SingleOrDefault(x => x.Capability == capability && x.SourceId == sourceId);
        Require(mapping is not null && mapping.WireMode == NeutralQueryContract.WireMode &&
            Sources().TryGetValue((capability, sourceId), out var source) && Usable(capability, source) && Supports(mapping, source), "source_unavailable");
        var (user, person) = await AccountAsync(userId, ct);
        var stored = mapping!.Mappings.Where(x => !x.ValueSource.StartsWith("runtime.", StringComparison.Ordinal)).Select(x =>
            new { x.Attribute, x.ValueSource, Value = StoredValue(user, person, x.ValueSource) }).ToArray();
        var deployed = Sources()[(capability, sourceId)];
        var fingerprint = Hash(new { mapping, user.Id, user.PersonId, user.SecurityStamp, user.ConcurrencyStamp,
                user.ModifiedAt, user.UpdatedAt, user.NativeFieldStateJson, PersonModifiedAt = person?.ModifiedAt,
                PersonNativeState = person?.NativeFieldStateJson, person?.IdentityVerifiedAt, person?.IdentityVerifiedBy, stored });
        // Retain the existing HTTP approval material; only direct reads add the directory scope contract.
        if (deployed.Directory is not null) fingerprint = Hash(new { fingerprint, deployed.DirectoryConfigurationFingerprint });
        return new(mapping, fingerprint,
            stored.All(x => NeutralQueryContract.TryNormalize(x.Attribute, x.Value, out _)));
    }
    private static string? StoredValue(ApplicationUser user, Person? person, string source) => source switch
    { "person.employeeId" => person?.EmployeeId, "user.employeeId" => user.EmployeeId, "person.nationalId" => person?.NationalId, "user.userName" => user.UserName, _ => null };

    private async Task<bool> HasNativeProvenanceAsync(ApplicationUser user, Person? person, QueryAttributeMappingDto mapping,
        NeutralQuerySourceOptions source, CancellationToken ct)
    {
        if (mapping.Attribute != "employeeId" || mapping.ValueSource is not ("person.employeeId" or "user.employeeId")) return false;
        if (mapping.ValueSource == "person.employeeId" && person is null) return false;
        try
        {
            var state = await nativeFields.GetTargetAsync(mapping.ValueSource == "person.employeeId" ? NativeFieldTarget.Person : NativeFieldTarget.User,
                mapping.ValueSource == "person.employeeId" ? person!.Id : user.Id, ct);
            var field = state.Fields.Single(x => x.Field == NativeField.EmployeeId);
            return !field.ManualFixed && field.Attributed && field.Owner is { } owner && owner.LocalAccountId == user.Id &&
                source.TrustedValueProviderNamespaces.Contains(owner.ProviderNamespace, StringComparer.Ordinal) && state.AvailableBindings.Contains(owner);
        }
        catch (NativeFieldPolicyException) { return false; }
    }

    private async Task<ApprovalDocument?> ReadApprovalAsync(string capability, string sourceId, Guid userId, CancellationToken ct)
    {
        var encrypted = await ReadAsync<string>(ApprovalKey(capability, sourceId, userId), ct);
        if (encrypted is null) return null;
        try { return JsonSerializer.Deserialize<ApprovalDocument>(_protector.Unprotect(encrypted), Json); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { throw new NeutralQueryException("invalid_stored_state"); }
    }
    private async Task<T?> ReadAsync<T>(string key, CancellationToken ct)
    {
        var value = await db.Settings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).SingleOrDefaultAsync(ct);
        if (value is null) return default;
        try
        {
            using var parsed = NeutralQueryJson.Parse(Encoding.UTF8.GetBytes(value), 65536);
            return parsed.RootElement.Deserialize<T>(Json) ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException) { throw new NeutralQueryException("invalid_stored_state"); }
    }
    private async Task WriteAsync<T>(string key, T value, CancellationToken ct)
    {
        var row = await db.Settings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) { row = new Setting { Id = Guid.NewGuid(), Key = key }; db.Settings.Add(row); }
        row.Value = Serialize(value);
        row.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    private async Task<T> MutateAsync<T>(Func<Task<T>> mutate, CancellationToken ct)
    {
        Require(db is DbContext context && context.Database.IsRelational(), "transaction_unavailable");
        var relational = (DbContext)db;
        Require(!relational.ChangeTracker.HasChanges(), "conflict");
        await using var transaction = await relational.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var committed = false;
        try { var result = await mutate(); await transaction.CommitAsync(ct); committed = true; return result; }
        catch (DbUpdateException) { throw new NeutralQueryException("conflict"); }
        catch (DbException) { throw new NeutralQueryException("conflict"); }
        finally { if (!committed) { await transaction.RollbackAsync(CancellationToken.None); relational.ChangeTracker.Clear(); } }
    }
    private static string ApprovalKey(string capability, string sourceId, Guid userId) => SettingKeys.NeutralQueryApprovalPrefix + Hash(new { capability, sourceId, userId });
    private static void Require(bool condition, string code) { if (!condition) throw new NeutralQueryException(code); }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));
    private static Guid Revision<T>(T value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))).AsSpan(0, 16));
    private sealed record DeployedSource(NeutralQuerySourceOptions Policy, DirectoryQueryTransport? Directory = null,
        string? DirectoryConfigurationFingerprint = null, bool DirectoryConfigurationValid = true);
    private sealed record ConfigurationDocument(Guid Revision, IReadOnlyList<QueryConfigurationDto> Configurations);
    private sealed record ApprovalDocument(Guid Revision, bool Approved, string ValueFingerprint);
    private sealed record ApprovalMaterial(QueryConfigurationDto Mapping, string Fingerprint, bool Complete);
}
