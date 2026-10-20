using System.Text.Json;
using Core.Application;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class NativeFieldPolicyService(
    IApplicationDbContext db, IOptions<NativeFieldPolicyOptions> options,
    IOptions<ProviderProofOptions> proofOptions, IOptions<DirectoryIntegrationOptions> directoryOptions)
    : INativeFieldPolicyService
{
    private static readonly NativeField[] Fields = Enum.GetValues<NativeField>();
    private const string NoClear = "Current source contracts do not provide authoritative native-field clear intent.";

    public async Task<IReadOnlyList<NativeFieldSourceStatusDto>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        var policies = await db.NativeFieldSourcePolicies.AsNoTracking().ToListAsync(cancellationToken);
        return Enum.GetValues<NativeFieldSource>().Select(source =>
        {
            var policy = policies.SingleOrDefault(x => x.Source == source.ToString());
            var enabled = IsEnabled(source);
            var providerNamespace = source == NativeFieldSource.Proof ? proofOptions.Value.TrustedProviderNamespace : null;
            return new NativeFieldSourceStatusDto(source, enabled, policy?.Paused == true,
                enabled ? null : "Deployment has not enabled and trusted this native-field source.",
                Fields, false, NoClear, policy?.Revision ?? Guid.Empty, providerNamespace,
                source == NativeFieldSource.Proof && policy?.FirstNameDisplayNameFallback == true &&
                string.Equals(policy.FirstNameFallbackProviderNamespace, providerNamespace, StringComparison.Ordinal));
        }).ToArray();
    }

    public async Task SetFirstNameFallbackAsync(NativeFieldSource source, NativeFieldFirstNameFallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(source == NativeFieldSource.Proof, "unsupported_source");
        Require(IsEnabled(source), "source_unavailable");
        Require(!string.IsNullOrWhiteSpace(request.ProviderNamespace) && request.ProviderNamespace.Length <= 200,
            "invalid_namespace");
        Require(string.Equals(request.ProviderNamespace, proofOptions.Value.TrustedProviderNamespace, StringComparison.Ordinal),
            "conflict");
        var row = await db.NativeFieldSourcePolicies.SingleOrDefaultAsync(x => x.Source == source.ToString(), cancellationToken);
        Require((row?.Revision ?? Guid.Empty) == request.ExpectedRevision, "conflict");
        if (row is null)
        {
            row = new NativeFieldSourcePolicy { Source = source.ToString() };
            db.NativeFieldSourcePolicies.Add(row);
        }
        row.FirstNameFallbackProviderNamespace = request.ProviderNamespace;
        row.FirstNameDisplayNameFallback = request.Enabled;
        row.Revision = Guid.NewGuid();
        await SaveAsync(row, cancellationToken);
    }

    public async Task SetPausedAsync(NativeFieldSource source, NativeFieldPauseRequest request, CancellationToken cancellationToken = default)
    {
        Require(Enum.IsDefined(source), "unsupported_source");
        Require(IsEnabled(source), "source_unavailable");
        var row = await db.NativeFieldSourcePolicies.SingleOrDefaultAsync(x => x.Source == source.ToString(), cancellationToken);
        Require((row?.Revision ?? Guid.Empty) == request.ExpectedRevision, "conflict");
        if (row is null)
        {
            row = new NativeFieldSourcePolicy { Source = source.ToString() };
            db.NativeFieldSourcePolicies.Add(row);
        }
        row.Paused = request.Paused;
        row.Revision = Guid.NewGuid();
        await SaveAsync(row, cancellationToken);
    }

    public async Task<NativeFieldTargetDto> GetTargetAsync(NativeFieldTarget target, Guid targetId, CancellationToken cancellationToken = default)
    {
        var entity = await FindTargetAsync(target, targetId, cancellationToken);
        var state = Read(entity);
        var bindings = await GetBindingsAsync(target, targetId, cancellationToken);
        return new(target, targetId, state.Revision, Fields.Select(field =>
        {
            var entry = state.Fields.GetValueOrDefault(field) ?? new FieldState();
            return new NativeFieldStateDto(field, GetValue(entity, field), entry.ManualFixed,
                entry.Attributed, entry.Owner, entry.AllowOverwrite);
        }).ToArray(), bindings);
    }

    public async Task UpdateAsync(NativeFieldTarget target, Guid targetId, NativeField field,
        NativeFieldUpdateRequest request, CancellationToken cancellationToken = default)
    {
        Require(Enum.IsDefined(field) && Enum.IsDefined(request.Operation), "unsupported_field_or_operation");
        Require(!request.AllowSourceClear, "source_clear_unsupported");
        var entity = await FindTargetAsync(target, targetId, cancellationToken);
        var state = Read(entity);
        Require(state.Revision == request.ExpectedRevision, "conflict");
        var entry = state.Fields.GetValueOrDefault(field) ?? new FieldState();
        switch (request.Operation)
        {
            case NativeFieldOperation.AssignSource:
                var identity = request.SourceIdentity;
                Require(identity is not null && await IsAvailableAsync(identity, cancellationToken), "source_unavailable");
                Require(await IsBoundAsync(identity!, target, targetId, cancellationToken), "binding_mismatch");
                // Assignment may select another source of the same account, but never transfer shared Person ownership.
                Require(entry.Owner is null || entry.Owner.LocalAccountId == identity!.LocalAccountId, "owner_mismatch");
                if (entry.Owner != identity) entry.Attributed = false;
                entry.Owner = identity;
                entry.AllowOverwrite = request.AllowOverwrite;
                // Assignment alone does not remove manual protection or claim an existing value came from upstream.
                break;
            case NativeFieldOperation.Manual:
                Require(request.ManualValue is not null, "manual_value_required");
                ValidateValue(field, request.ManualValue!, entity);
                SetValue(entity, field, request.ManualValue!);
                entry.ManualFixed = true;
                entry.Attributed = false;
                break;
            case NativeFieldOperation.Unlock:
                // Retain source, exact owner and overwrite choice; no identity inference on unlock.
                entry.ManualFixed = false;
                break;
        }
        state.Fields[field] = entry;
        Write(entity, state);
        await SaveAsync(entity, cancellationToken);
    }

    public async Task<bool> ApplySourceAsync(ApplicationUser user, Person? person, NativeFieldSourceIdentity identity,
        IReadOnlyDictionary<NativeField, string?> values, CancellationToken cancellationToken = default)
    {
        if (identity.LocalAccountId != user.Id || !await IsAvailableAsync(identity, cancellationToken) ||
            !await IsBoundAsync(identity, NativeFieldTarget.User, user.Id, cancellationToken)) return false;
        ValidateValues(values, user);
        // Proof values have already passed contract assurance validation. Never use Profile
        // snapshots, another source, or a stored nickname as fallback input.
        var firstNameFallback = false;
        if (identity.Source == NativeFieldSource.Proof &&
            string.IsNullOrWhiteSpace(values.GetValueOrDefault(NativeField.FirstName)) &&
            values.GetValueOrDefault(NativeField.Nickname) is { Length: <= 100 } displayName &&
            !string.IsNullOrWhiteSpace(displayName))
        {
            var policy = await db.NativeFieldSourcePolicies.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Source == identity.Source.ToString(), cancellationToken);
            firstNameFallback = policy?.FirstNameDisplayNameFallback == true &&
                string.Equals(policy.FirstNameFallbackProviderNamespace, identity.ProviderNamespace, StringComparison.Ordinal);
            if (firstNameFallback)
                values = new Dictionary<NativeField, string?>(values) { [NativeField.FirstName] = displayName };
        }
        var changed = ApplySource(user, identity, values, true, firstNameFallback);
        if (person is not null && user.PersonId == person.Id &&
            await IsBoundAsync(identity, NativeFieldTarget.Person, person.Id, cancellationToken))
        {
            // A shared Person needs an explicit operator assignment before its first source claim.
            var soleAccount = await db.Users.CountAsync(x => x.PersonId == person.Id, cancellationToken) == 1;
            changed |= ApplySource(person, identity, values, soleAccount, firstNameFallback);
        }
        return changed;
    }

    public void ApplyManual(ApplicationUser user, IReadOnlyDictionary<NativeField, string?> values) => ApplyManualCore(user, values);
    public void ApplyManual(Person person, IReadOnlyDictionary<NativeField, string?> values) => ApplyManualCore(person, values);

    public void ApplyUnattributed(ApplicationUser user, Person? person, IReadOnlyDictionary<NativeField, string?> values)
    {
        ValidateValues(values, user);
        ApplyUnattributedCore(user, values);
        // Browser identity is not an advertised Person source capability; never mutate a shared Person here.
        // Caller may initialize a new Person, but existing Person authority requires the supported bound sources.
    }

    private static void ApplyUnattributedCore(object entity, IReadOnlyDictionary<NativeField, string?> values)
    {
        var state = Read(entity);
        var changed = false;
        foreach (var (field, value) in values)
        {
            var entry = state.Fields.GetValueOrDefault(field);
            if (string.IsNullOrWhiteSpace(value) || entry?.ManualFixed == true || entry?.Owner is not null ||
                !string.IsNullOrWhiteSpace(GetValue(entity, field))) continue;
            SetValue(entity, field, value);
            changed = true;
        }
        if (changed) Write(entity, state);
    }

    private static void ApplyManualCore(object entity, IReadOnlyDictionary<NativeField, string?> values)
    {
        ValidateValues(values, entity);
        var state = Read(entity);
        var changed = false;
        foreach (var (field, value) in values)
        {
            if (value is null) continue; // Omitted/null is not an explicit edit; empty string is an intentional clear.
            var entry = state.Fields.GetValueOrDefault(field) ?? new FieldState();
            entry.ManualFixed = true;
            entry.Attributed = false;
            state.Fields[field] = entry;
            SetValue(entity, field, value);
            changed = true;
        }
        if (changed) Write(entity, state);
    }

    private static bool ApplySource(object entity, NativeFieldSourceIdentity identity,
        IReadOnlyDictionary<NativeField, string?> values, bool canClaim, bool firstNameFallback)
    {
        var state = Read(entity);
        var changed = false;
        foreach (var (field, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            // Proof display names can fit User but exceed Person's established 100-character limit.
            // Optional display data must neither fail authentication nor be truncated.
            if (entity is Person && field == NativeField.Nickname && value.Length > 100) continue;
            var entry = state.Fields.GetValueOrDefault(field) ?? new FieldState();
            if (entry.ManualFixed || entry.Owner is not null && entry.Owner != identity || entry.Owner is null && !canClaim) continue;
            if (!string.IsNullOrWhiteSpace(GetValue(entity, field)) &&
                (!entry.AllowOverwrite || field == NativeField.FirstName && firstNameFallback)) continue;
            entry.Owner = identity;
            entry.Attributed = true;
            state.Fields[field] = entry;
            SetValue(entity, field, value);
            changed = true;
        }
        if (changed) Write(entity, state);
        return changed;
    }

    private bool IsEnabled(NativeFieldSource source) => source switch
    {
        NativeFieldSource.Proof => options.Value.ProofEnabled && proofOptions.Value.Enabled &&
            !string.IsNullOrWhiteSpace(proofOptions.Value.TrustedProviderNamespace),
        NativeFieldSource.Stage1 => options.Value.Stage1Enabled && directoryOptions.Value.Enabled &&
            options.Value.TrustedStage1ProviderNamespaces.Any(x => !string.IsNullOrWhiteSpace(x)),
        _ => false
    };

    private async Task<bool> IsAvailableAsync(NativeFieldSourceIdentity identity, CancellationToken cancellationToken)
    {
        if (!IsEnabled(identity.Source) || identity.LocalAccountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(identity.ProviderNamespace) || identity.ProviderNamespace.Length > 200 ||
            string.IsNullOrWhiteSpace(identity.StableSubject) || identity.StableSubject.Length > 256) return false;
        var trusted = identity.Source == NativeFieldSource.Proof
            ? string.Equals(identity.ProviderNamespace, proofOptions.Value.TrustedProviderNamespace, StringComparison.Ordinal)
            : options.Value.TrustedStage1ProviderNamespaces.Contains(identity.ProviderNamespace, StringComparer.Ordinal);
        return trusted && !await db.NativeFieldSourcePolicies.AsNoTracking()
            .AnyAsync(x => x.Source == identity.Source.ToString() && x.Paused, cancellationToken);
    }

    private async Task<bool> IsBoundAsync(NativeFieldSourceIdentity identity, NativeFieldTarget target, Guid targetId,
        CancellationToken cancellationToken)
    {
        if (target == NativeFieldTarget.User && identity.LocalAccountId != targetId) return false;
        if (target == NativeFieldTarget.Person && !await db.Users.AsNoTracking()
            .AnyAsync(x => x.Id == identity.LocalAccountId && x.PersonId == targetId, cancellationToken)) return false;
        if (identity.Source == NativeFieldSource.Proof)
        {
            if (identity.DirectoryObjectId is not null) return false;
            // Check ordinal in memory after limiting by account; database collations must not weaken exact binding.
            var logins = await db.UserLogins.AsNoTracking().Where(x => x.UserId == identity.LocalAccountId).ToListAsync(cancellationToken);
            return logins.Any(x => x.LoginProvider == identity.ProviderNamespace && x.ProviderKey == identity.StableSubject);
        }
        if (identity.Source != NativeFieldSource.Stage1 || identity.DirectoryObjectId is null || identity.DirectoryObjectId == Guid.Empty)
            return false;
        var binding = await db.ProviderSubjectDirectoryBindings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.LocalAccountId == identity.LocalAccountId, cancellationToken);
        return binding is not null && binding.ProviderNamespace == identity.ProviderNamespace &&
            binding.StableSubject == identity.StableSubject && binding.DirectoryObjectId == identity.DirectoryObjectId;
    }

    private async Task<IReadOnlyList<NativeFieldSourceIdentity>> GetBindingsAsync(NativeFieldTarget target, Guid targetId, CancellationToken cancellationToken)
    {
        var accounts = db.Users.AsNoTracking().Where(x => target == NativeFieldTarget.User ? x.Id == targetId : x.PersonId == targetId);
        var ids = await accounts.Select(x => x.Id).ToListAsync(cancellationToken);
        var logins = await db.UserLogins.AsNoTracking().Where(x => ids.Contains(x.UserId)).ToListAsync(cancellationToken);
        var bindings = await db.ProviderSubjectDirectoryBindings.AsNoTracking().Where(x => ids.Contains(x.LocalAccountId)).ToListAsync(cancellationToken);
        return logins.Where(x => x.LoginProvider == proofOptions.Value.TrustedProviderNamespace)
            .Select(x => new NativeFieldSourceIdentity(NativeFieldSource.Proof, x.UserId, x.LoginProvider, x.ProviderKey))
            .Concat(bindings.Where(x => options.Value.TrustedStage1ProviderNamespaces.Contains(x.ProviderNamespace, StringComparer.Ordinal))
                .Select(x => new NativeFieldSourceIdentity(NativeFieldSource.Stage1, x.LocalAccountId, x.ProviderNamespace, x.StableSubject, x.DirectoryObjectId)))
            .ToArray();
    }

    private async Task<object> FindTargetAsync(NativeFieldTarget target, Guid id, CancellationToken cancellationToken)
    {
        Require(Enum.IsDefined(target) && id != Guid.Empty, "invalid_target");
        object? entity = target == NativeFieldTarget.User
            ? await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            : await db.Persons.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity ?? throw new NativeFieldPolicyException("not_found");
    }

    private async Task SaveAsync(object entity, CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            db.Detach(entity);
            throw new NativeFieldPolicyException("conflict");
        }
    }

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new NativeFieldPolicyException(code);
    }

    private static void ValidateValues(IReadOnlyDictionary<NativeField, string?> values, object entity)
    {
        Require(values.Count <= Fields.Length, "invalid_fields");
        foreach (var (field, value) in values)
        {
            Require(Enum.IsDefined(field), "unsupported_field");
            if (value is not null) ValidateValue(field, value, entity);
        }
    }

    private static void ValidateValue(NativeField field, string value, object entity)
    {
        var maximum = field switch
        {
            NativeField.Nickname when entity is ApplicationUser => 256,
            NativeField.EmployeeId => 50, NativeField.Department or NativeField.JobTitle => 200, _ => 100
        };
        Require(value.Length <= maximum, "value_too_long");
    }

    private static FieldDocument Read(object entity)
    {
        var json = entity is ApplicationUser user ? user.NativeFieldStateJson : ((Person)entity).NativeFieldStateJson;
        if (json is null) return new();
        try
        {
            var state = JsonSerializer.Deserialize<FieldDocument>(json);
            if (state is null || state.Fields is null || state.Fields.Count > Fields.Length ||
                state.Fields.Any(x => !Enum.IsDefined(x.Key) || x.Value is null)) throw new JsonException();
            return state;
        }
        catch (JsonException) { throw new NativeFieldPolicyException("invalid_stored_state"); }
    }

    private static void Write(object entity, FieldDocument state)
    {
        state.Revision = Guid.NewGuid();
        var json = JsonSerializer.Serialize(state);
        if (entity is ApplicationUser user) user.NativeFieldStateJson = json;
        else ((Person)entity).NativeFieldStateJson = json;
    }

    // Fixed allowlist, never a property name supplied by a client.
    private static string? GetValue(object entity, NativeField field) => (string?)Property(entity, field).GetValue(entity);
    private static void SetValue(object entity, NativeField field, string value) =>
        // Person EmployeeId has a filtered unique index. Multiple intentional blanks must remain possible.
        Property(entity, field).SetValue(entity, field == NativeField.EmployeeId && string.IsNullOrWhiteSpace(value) ? null : value);
    private static System.Reflection.PropertyInfo Property(object entity, NativeField field)
    {
        Require(Enum.IsDefined(field), "unsupported_field");
        return (entity is ApplicationUser ? typeof(ApplicationUser) : typeof(Person)).GetProperty(field.ToString())!;
    }

    private sealed class FieldDocument
    {
        public Guid Revision { get; set; }
        public Dictionary<NativeField, FieldState> Fields { get; set; } = [];
    }
    private sealed class FieldState
    {
        public bool ManualFixed { get; set; }
        public bool Attributed { get; set; }
        public NativeFieldSourceIdentity? Owner { get; set; }
        public bool AllowOverwrite { get; set; }
    }
}
