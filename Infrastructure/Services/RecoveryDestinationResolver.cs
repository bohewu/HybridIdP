using System.Globalization;
using Core.Application.Ports;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryDestinationResolver(
    ApplicationDbContext dbContext,
    IRecoveryDefaultDestinationEvaluator defaultEvaluator,
    IOptions<RecoveryEmailSelectionOptions> selectionOptions,
    IOptions<RecoveryVerificationPolicyOptions> verificationOptions,
    TimeProvider? timeProvider = null) : IRecoveryDestinationResolver
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<RecoveryDestinationResult> ResolveAsync(Guid localAccountId, CancellationToken cancellationToken = default)
    {
        var policy = verificationOptions.Value;
        var selection = selectionOptions.Value;
        if (!policy.Enabled) return Denied(RecoveryDestinationFailure.PolicyDisabled);

        try
        {
            var now = _timeProvider.GetUtcNow();
            var user = await dbContext.Users.AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == localAccountId, cancellationToken);
            if (user is null || !user.IsActive || user.IsDeleted ||
                user.LockoutEnabled && user.LockoutEnd > now)
                return Denied(RecoveryDestinationFailure.IneligibleAccount);
            if (user.PersonId is { } personId)
            {
                var person = await dbContext.Persons.AsNoTracking()
                    .SingleOrDefaultAsync(person => person.Id == personId, cancellationToken);
                if (person?.CanAuthenticate() != true) return Denied(RecoveryDestinationFailure.IneligibleAccount);
            }

            // Read persisted intent even after application rollback. Disabled flags never erase it.
            var preference = await dbContext.RecoveryEmailPreferences.AsNoTracking()
                .SingleOrDefaultAsync(preference => preference.LocalAccountId == localAccountId, cancellationToken);
            var mode = preference?.Mode ?? RecoveryEmailSelectionMode.Legacy;
            if (!Enum.IsDefined(mode) || preference?.AdministrativeBlockedAtUtc is not null ||
                mode == RecoveryEmailSelectionMode.Disabled || preference?.SelectionEpoch < 1)
                return Denied(RecoveryDestinationFailure.Blocked);

            var active = await dbContext.RecoveryEmails.AsNoTracking()
                .SingleOrDefaultAsync(record => record.LocalAccountId == localAccountId, cancellationToken);
            var epoch = preference?.SelectionEpoch ?? 1;
            var policyDigest = PolicyDigest(policy, selection);

            // UseDefault explicitly deselects the retained custom row. A broken UseCustom does not.
            if (mode != RecoveryEmailSelectionMode.UseDefault && active is not null &&
                active.Provenance != RecoveryEmailProvenance.SourceDefault)
            {
                if (!RecoveryProofSecurity.TryNormalizeAddress(active.Address, out var address, out var normalized) ||
                    normalized != active.NormalizedAddress || !Enum.IsDefined(active.Provenance) ||
                    active.UpdatedAtUtc > now || active.Version < 1)
                    return Denied(RecoveryDestinationFailure.InvalidCustom);

                if (active.VerifiedAtUtc is { } verified && verified <= now &&
                    active.Provenance != RecoveryEmailProvenance.SourceDefault)
                {
                    var kind = active.Provenance == RecoveryEmailProvenance.LegacyUnknown
                        ? RecoveryDestinationKind.Legacy : RecoveryDestinationKind.Custom;
                    var fingerprint = RecoveryDestinationBinding.ComputeDigest("recovery-custom-v1",
                        localAccountId.ToString("D"), active.Id.ToString("D"), address, normalized,
                        active.Provenance.ToString(), verified.ToString("O", CultureInfo.InvariantCulture));
                    // Row Version also changes for resend reservations. Destination version must not.
                    return Accepted(address, kind, fingerprint, active.UpdatedAtUtc.UtcTicks, active.Id);
                }

                // Preserve only the pre-feature bootstrap of an unverified legacy source row.
                // New pending changes are separate rows and are deliberately not read here.
                if (mode != RecoveryEmailSelectionMode.Legacy || active.VerifiedAtUtc is not null ||
                    active.Provenance != RecoveryEmailProvenance.LegacyUnknown)
                    return Denied(RecoveryDestinationFailure.InvalidCustom);
                if (user.RecoverySourceBootstrapRevokedAtUtc is not null || preference?.SourceDefaultOptOutAtUtc is not null)
                    return Denied(RecoveryDestinationFailure.SourceOptOut);
                var legacySource = await defaultEvaluator.EvaluateDefaultAsync(localAccountId, cancellationToken);
                if (legacySource is null || !legacySource.BootstrapActive ||
                    !RecoveryProofSecurity.TryNormalizeAddress(legacySource.Address, out _, out var sourceNormalized) ||
                    normalized != sourceNormalized)
                    return Denied(RecoveryDestinationFailure.InvalidCustom);
                return Accepted(legacySource.Address, RecoveryDestinationKind.TrustedDefault,
                    legacySource.Fingerprint, legacySource.Version, null);
            }

            if (mode == RecoveryEmailSelectionMode.UseCustom)
                return Denied(RecoveryDestinationFailure.InvalidCustom);
            if (user.RecoverySourceBootstrapRevokedAtUtc is not null || preference?.SourceDefaultOptOutAtUtc is not null)
                return Denied(RecoveryDestinationFailure.SourceOptOut);
            var source = await defaultEvaluator.EvaluateDefaultAsync(localAccountId, cancellationToken);
            if (source is null || !(selection.Enabled && selection.TrustedDefaultFallbackEnabled || source.BootstrapActive))
                return Denied(RecoveryDestinationFailure.NoEligibleDefault);
            return Accepted(source.Address, RecoveryDestinationKind.TrustedDefault, source.Fingerprint, source.Version, null);

            RecoveryDestinationResult Accepted(string address, RecoveryDestinationKind kind, string fingerprint, long version, Guid? recordId) =>
                new(RecoveryDestinationFailure.None, new RecoveryDestination(localAccountId, address,
                    RecoveryProofSecurity.MaskAddress(address), kind, fingerprint, version, epoch, policyDigest, recordId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // A failed custom lookup is never absence. Do not log recipient or database exception data.
            return Denied(RecoveryDestinationFailure.LookupFailed);
        }
    }

    private static RecoveryDestinationResult Denied(RecoveryDestinationFailure failure) => new(failure);

    private static string PolicyDigest(RecoveryVerificationPolicyOptions policy, RecoveryEmailSelectionOptions selection) =>
        RecoveryDestinationBinding.ComputeDigest("recovery-selection-policy-v1", policy.Enabled.ToString(),
            policy.CurrentPeriodId, policy.EffectiveAtUtc?.ToString("O", CultureInfo.InvariantCulture),
            policy.GraceEndsAtUtc?.ToString("O", CultureInfo.InvariantCulture), policy.BootstrapEnabled.ToString(),
            policy.BootstrapUntilUtc?.ToString("O", CultureInfo.InvariantCulture), policy.AcceptSourceVerifiedEmails.ToString(),
            policy.AcceptPolicyTrustedEmails.ToString(), selection.Enabled.ToString(), selection.TrustedDefaultFallbackEnabled.ToString());
}
