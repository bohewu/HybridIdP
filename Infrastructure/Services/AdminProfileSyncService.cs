using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>One-account, reviewed, fill-only operation. It never participates in sign-in or claims projection.</summary>
public sealed class AdminProfileSyncService(
    ApplicationDbContext db, INativeFieldPolicyService nativeFields, IProviderNativeProfileClient client,
    IOptions<ProviderNativeProfileOptions> options, IDataProtectionProvider protection,
    ILookupNormalizer normalizer, IAuditService audit, TimeProvider? timeProvider = null) : IAdminProfileSyncService
{
    private readonly IDataProtector _protector = protection.CreateProtector("HybridIdP.AdminProfileSync.Preview.v1");
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<ProfileSyncStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return new(false, "disabled");
        if (!options.Value.TryGetEndpoint(out _)) return new(false, "configuration_invalid");
        var source = (await nativeFields.GetSourcesAsync(cancellationToken)).Single(x => x.Source == NativeFieldSource.Proof);
        if (!source.Enabled) return new(false, "source_disabled");
        if (source.Paused) return new(false, "source_paused");
        if (source.ProviderNamespace != options.Value.TrustedProviderNamespace) return new(false, "source_namespace_mismatch");
        return new(true, null);
    }

    public async Task<ProfileSyncPreviewDto> PreviewAsync(string actorKey, Guid userId, CancellationToken cancellationToken = default)
    {
        RequireActor(actorKey);
        var context = await LoadAsync(userId, cancellationToken);
        if (context.Reason is not null) return Preview(context, [], null, context.Reason);
        var source = await client.FetchAsync(Request(context.Identity!), cancellationToken);
        var plan = await PlanAsync(context, source, cancellationToken);
        var token = HasChanges(plan.Changes)
            ? _protector.Protect(JsonSerializer.Serialize(new PreviewReceipt(actorKey, userId,
                _time.GetUtcNow().AddMinutes(5), plan.Fingerprint))) : null;
        return Preview(context, plan.Changes, token, null);
    }

    public async Task<ProfileSyncApplyResult> ApplyAsync(string actorKey, Guid userId, string previewToken,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actorKey);
        var receipt = ReadReceipt(actorKey, userId, previewToken);
        // No network request is made while holding database locks.
        var initial = await LoadAsync(userId, cancellationToken);
        if (initial.Reason is not null) throw new ProfileSyncException("preview_stale");
        var source = await client.FetchAsync(Request(initial.Identity!), cancellationToken);
        if (!db.Database.IsRelational()) throw new ProfileSyncException("transaction_unavailable");
        // This operation must not flush mutations staged by another operation on the same scope.
        if (db.ChangeTracker.HasChanges()) throw new ProfileSyncException("preview_stale");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var committed = false;
        try
        {
            var context = await LoadAsync(userId, ct);
            if (context.Reason is not null || context.Identity != initial.Identity ||
                receipt.ExpiresAt <= _time.GetUtcNow()) throw new ProfileSyncException("preview_stale");
            // LoadAsync is detached in both modes. Attach the untouched rows before preparing the
            // plan so EF retains the actual concurrency originals and updates only changed columns.
            DetachReadCopies(context.User.Id, context.Person!.Id);
            db.Users.Attach(context.User);
            db.Persons.Attach(context.Person);
            var plan = await PlanAsync(context, source, ct);
            if (receipt.Fingerprint != plan.Fingerprint) throw new ProfileSyncException("preview_stale");
            var userFields = ChangedFields(plan.Changes, "User");
            var personFields = ChangedFields(plan.Changes, "Person");
            if (userFields.Length == 0 && personFields.Length == 0) throw new ProfileSyncException("preview_stale");
            context.User.ConcurrencyStamp = Guid.NewGuid().ToString();
            await db.SaveChangesAsync(ct);
            await audit.LogAdministrativeEventAsync("NativeProfileSynchronized", "User", userId.ToString(),
                JsonSerializer.Serialize(new { personId = context.Person.Id, changedUserFields = userFields,
                    changedPersonFields = personFields, changedFieldCount = userFields.Length + personFields.Length }), ct);
            await transaction.CommitAsync(ct);
            committed = true;
            return new("applied", userFields, personFields);
        }
        catch (DbUpdateException) { throw new ProfileSyncException("preview_stale"); }
        catch (DbException) { throw new ProfileSyncException("preview_stale"); }
        catch (NativeFieldPolicyException) { throw new ProfileSyncException("preview_stale"); }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                finally
                {
                    // Audit failures can leave staged inserts/deletes too. The request began with
                    // no pending writes; none of this failed unit may be flushed by a later save.
                    db.ChangeTracker.Clear();
                }
            }
        }
    }

    private async Task<SyncContext> LoadAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new ProfileSyncException("user_not_found");
        var status = await GetStatusAsync(ct);
        if (!status.Enabled) return new(user, null, null, [], null, status.Reason);
        var logins = await db.UserLogins.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        // Evaluate ordinal equality after the bounded lookup: SQL collation is not identity authority.
        var bindings = logins.Where(x => string.Equals(x.LoginProvider, options.Value.TrustedProviderNamespace,
            StringComparison.Ordinal)).ToArray();
        if (bindings.Length != 1) return new(user, null, null, [], null,
            bindings.Length == 0 ? "binding_missing" : "binding_ambiguous");
        var binding = bindings[0];
        if (!ProviderNativeProfileContract.IsValidIdentity(binding.LoginProvider, binding.ProviderKey))
            return new(user, null, null, [], null, "binding_missing");
        var identity = new NativeFieldSourceIdentity(NativeFieldSource.Proof, userId, binding.LoginProvider, binding.ProviderKey);
        var person = user.PersonId is { } personId
            ? await db.Persons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == personId, ct) : null;
        if (person is null) return new(user, null, identity, [], null, "person_missing");
        var accounts = await db.Users.AsNoTracking().Where(x => x.PersonId == person.Id).Select(x => x.Id).ToListAsync(ct);
        var policy = await db.NativeFieldSourcePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Source == "Proof", ct);
        return new(user, person, identity, accounts.OrderBy(x => x).ToArray(), policy, null);
    }

    private async Task<SyncPlan> PlanAsync(SyncContext context, ProviderNativeProfileResponse source, CancellationToken ct)
    {
        if (source.ProviderNamespace != context.Identity!.ProviderNamespace ||
            source.StableSubject != context.Identity.StableSubject) throw new ProfileSyncException("source_invalid_response");
        var user = context.User;
        var person = context.Person!;
        // Capture before preparing changes; policy writes intentionally generate new random revisions.
        var localFingerprint = Hash(new
        {
            User = new { user.Id, user.UserName, user.PersonId, user.ConcurrencyStamp, user.SecurityStamp,
                user.NativeFieldStateJson, user.Nickname, user.FirstName, user.LastName, user.Department,
                user.JobTitle, user.EmployeeId, user.Email, user.NormalizedEmail, user.EmailConfirmed, user.EmailMfaEnabled },
            Person = new { person.Id, person.NativeFieldStateJson, person.Nickname, person.FirstName, person.LastName,
                person.Department, person.JobTitle, person.EmployeeId, person.Email, person.Status, person.IsDeleted },
            context.Identity, context.Accounts, context.Policy, Configuration = options.Value,
            Mappings = await nativeFields.GetMappingsAsync(ct)
        });
        var profile = source.Profile;
        var changes = (await nativeFields.ApplyMissingSourceAsync(user, person, context.Identity,
            new Dictionary<NativeField, string?>
            {
                [NativeField.Nickname] = profile.DisplayName, [NativeField.FirstName] = profile.GivenName,
                [NativeField.LastName] = profile.Surname, [NativeField.Department] = profile.Department,
                [NativeField.JobTitle] = profile.Title, [NativeField.EmployeeId] = profile.EmployeeId
            }, ct)).ToList();
        if (changes.Count != 12) throw new ProfileSyncException("preview_stale");
        ApplyEmail(context, source, changes);
        return new(changes, Hash(new { localFingerprint, Source = source, Changes = changes }));
    }

    private void ApplyEmail(SyncContext context, ProviderNativeProfileResponse source, List<ProfileSyncChangeDto> changes)
    {
        var user = context.User;
        var person = context.Person!;
        var email = source.Profile.Email;
        var valid = !string.IsNullOrWhiteSpace(email) && source.Profile.AssuredFields.Contains(AssuredProfileField.Email) &&
            ProviderNativeProfileContract.IsValidEmail(email);
        var userEmail = user.Email;
        var action = user.EmailMfaEnabled ? "mfa_protected" :
            !string.IsNullOrWhiteSpace(userEmail) ? "preserve" :
            user.EmailConfirmed ? "inconsistent_email" : valid ? "fill" : "missing_source";
        if (action == "fill")
        {
            user.Email = email;
            user.NormalizedEmail = normalizer.NormalizeEmail(email);
        }
        changes.Add(new("User", "Email", userEmail, email, user.Email, action));
        var personEmail = person.Email;
        action = !string.IsNullOrWhiteSpace(personEmail) ? "preserve" :
            context.Accounts.Length != 1 ? "shared_person" : valid ? "fill" : "missing_source";
        if (action == "fill") person.Email = email;
        changes.Add(new("Person", "Email", personEmail, email, person.Email, action));
        var confirmed = user.EmailConfirmed;
        var trusted = source.EmailTrustOrigin is "SourceVerified" or "PolicyTrusted" &&
            options.Value.TrustedEmailProviderNamespaces.Contains(source.ProviderNamespace, StringComparer.Ordinal);
        action = user.EmailMfaEnabled ? "mfa_protected" : confirmed ? "preserve" : !valid ? "missing_source" :
            !trusted ? "email_untrusted" : !string.Equals(user.Email, email, StringComparison.Ordinal) ? "email_mismatch" : "confirm";
        if (action == "confirm") user.EmailConfirmed = true;
        changes.Add(new("User", "EmailConfirmed", confirmed ? "true" : "false", valid && trusted ? "true" : null,
            user.EmailConfirmed ? "true" : "false", action));
    }

    private PreviewReceipt ReadReceipt(string actorKey, Guid userId, string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) throw new CryptographicException();
            var receipt = JsonSerializer.Deserialize<PreviewReceipt>(_protector.Unprotect(token));
            if (receipt is null || receipt.ActorKey != actorKey || receipt.UserId != userId ||
                receipt.ExpiresAt <= _time.GetUtcNow() || receipt.ExpiresAt > _time.GetUtcNow().AddMinutes(5))
                throw new CryptographicException();
            return receipt;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            throw new ProfileSyncException("preview_invalid");
        }
    }

    private void DetachReadCopies(Guid userId, Guid personId)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(x =>
            x.Entity is ApplicationUser user && user.Id == userId || x.Entity is Person person && person.Id == personId).ToArray())
            entry.State = EntityState.Detached;
    }

    private static ProfileSyncPreviewDto Preview(SyncContext context, IReadOnlyList<ProfileSyncChangeDto> changes,
        string? token, string? reason) => new(context.User.Id, context.User.UserName, context.User.PersonId,
            context.Identity?.ProviderNamespace, reason is not null ? "blocked" : HasChanges(changes) ? "ready" : "no_changes",
            reason, changes, token);
    private static ProviderNativeProfileRequest Request(NativeFieldSourceIdentity identity) =>
        new(ProviderNativeProfileContract.Version, identity.ProviderNamespace, identity.StableSubject);
    private static bool HasChanges(IReadOnlyList<ProfileSyncChangeDto> changes) => changes.Any(x => x.Action is "fill" or "confirm");
    private static string[] ChangedFields(IReadOnlyList<ProfileSyncChangeDto> changes, string target) =>
        changes.Where(x => x.Target == target && x.Action is "fill" or "confirm").Select(x => x.Field).ToArray();
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static void RequireActor(string actorKey)
    {
        if (string.IsNullOrWhiteSpace(actorKey)) throw new ProfileSyncException("unauthorized");
    }
    private sealed record SyncContext(ApplicationUser User, Person? Person, NativeFieldSourceIdentity? Identity,
        Guid[] Accounts, NativeFieldSourcePolicy? Policy, string? Reason);
    private sealed record SyncPlan(IReadOnlyList<ProfileSyncChangeDto> Changes, string Fingerprint);
    private sealed record PreviewReceipt(string ActorKey, Guid UserId, DateTimeOffset ExpiresAt, string Fingerprint);
}
