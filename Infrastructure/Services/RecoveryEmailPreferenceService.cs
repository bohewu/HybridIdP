using System.Data;
using System.Text.Json;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryEmailPreferenceService(ApplicationDbContext db, IRecoveryProofAuthorizer authorizer,
    RecoveryEmailStepUpService stepUp, IRecoveryDestinationResolver resolver,
    IRecoveryDefaultDestinationEvaluator defaults, RecoveryNotificationService notifications,
    IPasswordHasher<ApplicationUser> hasher, IDataProtectionProvider protection,
    IOptions<RecoveryEmailSelectionOptions> selection, IOptions<CredentialMigrationOptions> otpOptions,
    RecoveryThrottleService throttle, TimeProvider? timeProvider = null) : IRecoveryEmailPreferenceService
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly IDataProtector _confirmation = protection.CreateProtector("RecoveryPreference.UseDefault.v1");
    public bool Enabled => selection.Value.Enabled && selection.Value.SelfServiceEnabled;

    public async Task<RecoveryPreferenceStatus> GetStatusAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var preference = await db.RecoveryEmailPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.LocalAccountId == accountId, cancellationToken);
        if (!selection.Value.Enabled && preference is null)
            return new(false, "unavailable", null, null, null, null, null);
        var effective = await resolver.ResolveAsync(accountId, cancellationToken);
        var pending = await PendingAsync(accountId, cancellationToken);
        // Reading persisted intent must not depend on whether mutation/reauthentication is enabled.
        var maskedDefault = effective.Destination?.Kind == RecoveryDestinationKind.TrustedDefault
            ? effective.Destination.MaskedAddress : null;
        if (maskedDefault is null && selection.Value.Enabled && selection.Value.TrustedDefaultFallbackEnabled &&
            preference?.AdministrativeBlockedAtUtc is null && preference?.SourceDefaultOptOutAtUtc is null &&
            preference?.Mode != RecoveryEmailSelectionMode.Disabled &&
            effective.Failure is not (RecoveryDestinationFailure.IneligibleAccount or RecoveryDestinationFailure.LookupFailed or
                RecoveryDestinationFailure.PolicyDisabled or RecoveryDestinationFailure.Blocked or RecoveryDestinationFailure.SourceOptOut))
        {
            var source = await defaults.EvaluateDefaultAsync(accountId, cancellationToken);
            if (source is not null) maskedDefault = RecoveryProofSecurity.MaskAddress(source.Address);
        }
        var isPending = pending?.IsPending(_time.GetUtcNow()) == true;
        return new(Enabled, preference?.Mode.ToString() ?? "Legacy", effective.Destination?.MaskedAddress,
            maskedDefault,
            isPending ? RecoveryProofSecurity.MaskAddress(pending!.Address) : null,
            isPending ? pending!.ExpiresAtUtc : null, isPending ? pending!.NextSendAllowedAtUtc : null);
    }

    public async Task<RecoveryProofOutcome> BeginAsync(Guid accountId, string candidate, RecoveryPreferenceContext context, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return RecoveryProofOutcome.Unavailable;
        if (!await AuthorizedAsync(accountId, cancellationToken)) return RecoveryProofOutcome.Unauthorized;
        if (!RecoveryProofSecurity.TryNormalizeAddress(candidate, out var address, out var normalized)) return RecoveryProofOutcome.Invalid;
        if (!await throttle.TakeAsync("preference-begin", "subject", accountId.ToString("D"), 5, TimeSpan.FromMinutes(15), cancellationToken)) return RecoveryProofOutcome.Cooldown;
        RecoveryEmailPendingChange pending;
        var code = RecoveryProofSecurity.GenerateNumericCode();
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            try
            {
                var grant = await stepUp.ValidateAsync(accountId, context, false, cancellationToken);
                if (grant is null) return RecoveryProofOutcome.Unauthorized;
                var preference = await PreferenceAsync(accountId, cancellationToken);
                if (preference.AdministrativeBlockedAtUtc is not null || preference.Mode == RecoveryEmailSelectionMode.Disabled) return RecoveryProofOutcome.Unavailable;
                var old = await PendingAsync(accountId, cancellationToken);
                if (old is not null) old.Revoke(_time.GetUtcNow());
                var now = _time.GetUtcNow();
                if (!grant.TryConsume(accountId, grant.SecurityStamp, context.ContextHash, context.CsrfHash, grant.AuthorityBinding, now)) return RecoveryProofOutcome.Replayed;
                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == accountId, cancellationToken);
                pending = new RecoveryEmailPendingChange(accountId, address, normalized,
                    hasher.HashPassword(user, BindCode(code, context)), preference.SelectionEpoch, grant.SecurityStamp,
                    context.ContextHash, context.CsrfHash, grant.Id, now,
                    now.AddMinutes(Math.Clamp(otpOptions.Value.RecoveryOtpLifetimeMinutes, 1, 10)),
                    now.AddSeconds(Math.Max(1, otpOptions.Value.RecoveryOtpResendCooldownSeconds)),
                    Math.Clamp(otpOptions.Value.RecoveryOtpMaxAttempts, 1, 10));
                // Persist revocation before inserting the replacement under the active-row unique index.
                await db.SaveChangesAsync(cancellationToken);
                db.RecoveryEmailChangeRequests.Add(pending);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException) { await tx.RollbackAsync(cancellationToken); db.ChangeTracker.Clear(); return RecoveryProofOutcome.Replayed; }
        }
        return await DeliverAsync(pending, code, cancellationToken);
    }

    public async Task<RecoveryProofOutcome> ResendAsync(Guid accountId, RecoveryPreferenceContext context, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return RecoveryProofOutcome.Unavailable;
        if (!await AuthorizedAsync(accountId, cancellationToken)) return RecoveryProofOutcome.Unauthorized;
        var pending = await PendingAsync(accountId, cancellationToken);
        if (!await MatchesAsync(accountId, pending, context, cancellationToken)) return RecoveryProofOutcome.Unauthorized;
        var now = _time.GetUtcNow();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == accountId, cancellationToken);
        var code = RecoveryProofSecurity.GenerateNumericCode();
        if (!pending!.TryReserveResend(hasher.HashPassword(user, BindCode(code, context)), now,
            now.AddSeconds(Math.Max(1, otpOptions.Value.RecoveryOtpResendCooldownSeconds)))) return RecoveryProofOutcome.Cooldown;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return RecoveryProofOutcome.Replayed; }
        return await DeliverAsync(pending, code, cancellationToken);
    }

    public async Task<RecoveryProofOutcome> VerifyAsync(Guid accountId, string code, RecoveryPreferenceContext context, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return RecoveryProofOutcome.Unavailable;
        if (!await AuthorizedAsync(accountId, cancellationToken)) return RecoveryProofOutcome.Unauthorized;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var pending = await PendingAsync(accountId, cancellationToken);
            if (!await MatchesAsync(accountId, pending, context, cancellationToken)) return RecoveryProofOutcome.Unauthorized;
            var now = _time.GetUtcNow();
            if (!pending!.TryReserveAttempt(now)) return RecoveryProofOutcome.Exhausted;
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == accountId, cancellationToken);
            var valid = code is { Length: 6 } && code.All(char.IsAsciiDigit) &&
                hasher.VerifyHashedPassword(user, pending.CodeHash, BindCode(code, context)) != PasswordVerificationResult.Failed;
            if (!valid)
            {
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return RecoveryProofOutcome.Invalid;
            }
            var preference = await PreferenceAsync(accountId, cancellationToken);
            var old = await resolver.ResolveAsync(accountId, cancellationToken);
            if (!pending.TryConsume(preference.SelectionEpoch, now) ||
                !preference.TrySelect(RecoveryEmailSelectionMode.UseCustom, pending.ExpectedSelectionEpoch, now)) return RecoveryProofOutcome.Replayed;
            var active = await db.RecoveryEmails.SingleOrDefaultAsync(e => e.LocalAccountId == accountId, cancellationToken);
            if (active is null)
            {
                active = new RecoveryEmailRecord(accountId, pending.Address, pending.NormalizedAddress, now);
                db.RecoveryEmails.Add(active);
            }
            active.ActivateVerifiedCustom(pending.Address, pending.NormalizedAddress, now, RecoveryEmailProvenance.UserVerified);
            await RevokePreviousAsync(accountId, cancellationToken);
            notifications.Enqueue(accountId, preference.SelectionEpoch, old.Destination?.Address, pending.Address, false);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return RecoveryProofOutcome.Success;
        }
        catch (DbUpdateException) { await tx.RollbackAsync(cancellationToken); db.ChangeTracker.Clear(); return RecoveryProofOutcome.Replayed; }
    }

    public async Task<RecoveryProofOutcome> CancelAsync(Guid accountId, RecoveryPreferenceContext context, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return RecoveryProofOutcome.Unavailable;
        if (!await AuthorizedAsync(accountId, cancellationToken) ||
            await stepUp.ValidateAsync(accountId, context, true, cancellationToken) is null) return RecoveryProofOutcome.Unauthorized;
        var pending = await PendingAsync(accountId, cancellationToken);
        if (pending is null) return RecoveryProofOutcome.Missing;
        // Cancellation discards only this account's candidate; it cannot select or revoke an effective address.
        pending.Revoke(_time.GetUtcNow());
        try { await db.SaveChangesAsync(cancellationToken); return RecoveryProofOutcome.Success; }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return RecoveryProofOutcome.Replayed; }
    }

    public async Task<RecoveryDefaultConfirmation?> PrepareDefaultAsync(Guid accountId, RecoveryPreferenceContext context, CancellationToken cancellationToken = default)
    {
        if (!Enabled || !await AuthorizedAsync(accountId, cancellationToken) ||
            await stepUp.ValidateAsync(accountId, context, false, cancellationToken) is null) return null;
        var source = await DefaultAsync(accountId, cancellationToken);
        if (source is null) return null;
        var preference = await db.RecoveryEmailPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.LocalAccountId == accountId, cancellationToken);
        var data = new DefaultConfirmation(accountId, context, source.Fingerprint, source.Version,
            preference?.SelectionEpoch ?? 1, _time.GetUtcNow().AddMinutes(5));
        return new(RecoveryProofSecurity.MaskAddress(source.Address), _confirmation.Protect(JsonSerializer.Serialize(data)));
    }

    public async Task<RecoveryProofOutcome> UseDefaultAsync(Guid accountId, string confirmation, RecoveryPreferenceContext context, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return RecoveryProofOutcome.Unavailable;
        if (!await AuthorizedAsync(accountId, cancellationToken)) return RecoveryProofOutcome.Unauthorized;
        DefaultConfirmation? data;
        try { data = JsonSerializer.Deserialize<DefaultConfirmation>(_confirmation.Unprotect(confirmation)); }
        catch { return RecoveryProofOutcome.Invalid; }
        if (data is null || data.AccountId != accountId || data.Context != context || data.ExpiresAtUtc <= _time.GetUtcNow()) return RecoveryProofOutcome.Invalid;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var grant = await stepUp.ValidateAsync(accountId, context, false, cancellationToken);
            if (grant is null) return RecoveryProofOutcome.Unauthorized;
            var source = await DefaultAsync(accountId, cancellationToken);
            if (source is null || source.Fingerprint != data.Fingerprint || source.Version != data.Version) return RecoveryProofOutcome.Unavailable;
            var preference = await PreferenceAsync(accountId, cancellationToken);
            var old = await resolver.ResolveAsync(accountId, cancellationToken);
            var now = _time.GetUtcNow();
            if (!grant.TryConsume(accountId, grant.SecurityStamp, context.ContextHash, context.CsrfHash, grant.AuthorityBinding, now) ||
                !preference.TrySelect(RecoveryEmailSelectionMode.UseDefault, data.Epoch, now)) return RecoveryProofOutcome.Replayed;
            await RevokePreviousAsync(accountId, cancellationToken);
            notifications.Enqueue(accountId, preference.SelectionEpoch, old.Destination?.Address, source.Address, true);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return RecoveryProofOutcome.Success;
        }
        catch (DbUpdateException) { await tx.RollbackAsync(cancellationToken); db.ChangeTracker.Clear(); return RecoveryProofOutcome.Replayed; }
    }

    private async Task<bool> AuthorizedAsync(Guid accountId, CancellationToken ct) =>
        await authorizer.IsSelfServiceAuthorizedAsync(accountId, ct) && await stepUp.ResolveAsync(accountId, ct) is not null;

    private Task<RecoveryEmailPendingChange?> PendingAsync(Guid accountId, CancellationToken ct) =>
        db.RecoveryEmailChangeRequests.SingleOrDefaultAsync(p => p.LocalAccountId == accountId && p.ConsumedAtUtc == null && p.RevokedAtUtc == null, ct);

    private async Task<RecoveryEmailPreference> PreferenceAsync(Guid accountId, CancellationToken ct)
    {
        var preference = await db.RecoveryEmailPreferences.SingleOrDefaultAsync(p => p.LocalAccountId == accountId, ct);
        if (preference is not null) return preference;
        preference = new RecoveryEmailPreference(accountId, _time.GetUtcNow());
        db.RecoveryEmailPreferences.Add(preference);
        return preference;
    }

    private async Task<bool> MatchesAsync(Guid accountId, RecoveryEmailPendingChange? pending, RecoveryPreferenceContext context, CancellationToken ct)
    {
        var grant = await stepUp.ValidateAsync(accountId, context, true, ct);
        var preference = await db.RecoveryEmailPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.LocalAccountId == accountId, ct);
        return pending is not null && pending.IsPending(_time.GetUtcNow()) && grant is not null && grant.ConsumedAtUtc is not null &&
            pending.StepUpGrantId == grant.Id && pending.ContextHash == context.ContextHash && pending.CsrfHash == context.CsrfHash &&
            pending.SecurityStamp == grant.SecurityStamp && pending.ExpectedSelectionEpoch == (preference?.SelectionEpoch ?? 1) &&
            preference?.AdministrativeBlockedAtUtc is null && preference?.Mode != RecoveryEmailSelectionMode.Disabled;
    }

    private async Task<RecoveryDefaultDestination?> DefaultAsync(Guid accountId, CancellationToken ct)
    {
        if (!selection.Value.TrustedDefaultFallbackEnabled || await stepUp.ResolveAsync(accountId, ct) is null) return null;
        var preference = await db.RecoveryEmailPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.LocalAccountId == accountId, ct);
        if (preference?.AdministrativeBlockedAtUtc is not null || preference?.SourceDefaultOptOutAtUtc is not null ||
            preference?.Mode == RecoveryEmailSelectionMode.Disabled) return null;
        return await defaults.EvaluateDefaultAsync(accountId, ct);
    }

    private async Task<RecoveryProofOutcome> DeliverAsync(RecoveryEmailPendingChange pending, string code, CancellationToken ct)
    {
        var sent = await notifications.SendCandidateAsync(pending.Address, code, ct);
        if (sent) pending.TryMarkDelivered(_time.GetUtcNow());
        else pending.Revoke(_time.GetUtcNow());
        try { await db.SaveChangesAsync(ct); return sent ? RecoveryProofOutcome.Success : RecoveryProofOutcome.Unavailable; }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return RecoveryProofOutcome.Replayed; }
    }

    private async Task RevokePreviousAsync(Guid accountId, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        foreach (var item in await db.RecoveryPrecheckGrants.Where(g => g.LocalAccountId == accountId && g.RevokedAtUtc == null && g.ConsumedAtUtc == null).ToListAsync(ct)) item.Revoke(now);
        foreach (var item in await db.RecoveryProofChallenges.Where(g => g.LocalAccountId == accountId && g.RevokedAtUtc == null && g.ConsumedAtUtc == null).ToListAsync(ct)) item.Revoke(now);
        foreach (var item in await db.RecoveryResetApprovals.Where(g => g.LocalAccountId == accountId && g.RevokedAtUtc == null && g.ConsumedAtUtc == null).ToListAsync(ct)) item.Revoke(now);
        foreach (var item in await db.NativeRecoveryResetApprovals.Where(g => g.LocalAccountId == accountId && g.RevokedAtUtc == null && g.ConsumedAtUtc == null).ToListAsync(ct)) item.Revoke(now);
        foreach (var item in await db.RecoveryEmailChangeRequests.Where(g => g.LocalAccountId == accountId && g.RevokedAtUtc == null && g.ConsumedAtUtc == null).ToListAsync(ct)) item.Revoke(now);
        foreach (var item in await db.RecoveryStepUpGrants.Where(g => g.LocalAccountId == accountId && g.RevokedAtUtc == null && g.ConsumedAtUtc == null).ToListAsync(ct)) item.Revoke(now);
    }

    private static string BindCode(string code, RecoveryPreferenceContext context) =>
        RecoveryProofSecurity.BindToContext(code, context.ContextHash, context.CsrfHash, context.StepUpGrantId.ToString("D"));
    private sealed record DefaultConfirmation(Guid AccountId, RecoveryPreferenceContext Context, string Fingerprint,
        long Version, long Epoch, DateTimeOffset ExpiresAtUtc);
}
