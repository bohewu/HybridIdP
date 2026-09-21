using Core.Application;
using Core.Application.Ports;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryVerificationPolicyEvaluator : IRecoveryVerificationPolicyEvaluator
{
    private readonly IApplicationDbContext _dbContext;
    private readonly RecoveryVerificationPolicyOptions _options;
    private readonly TimeProvider _timeProvider;

    public RecoveryVerificationPolicyEvaluator(
        IApplicationDbContext dbContext,
        IOptions<RecoveryVerificationPolicyOptions> options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RecoveryVerificationPolicyDecision> EvaluateAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return DisabledDecision();
        }

        var bindingIds = await _dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
            .Where(binding => binding.LocalAccountId == localAccountId)
            .Select(binding => binding.Id)
            .ToListAsync(cancellationToken);
        var snapshots = await _dbContext.ProviderMetadataSnapshots.AsNoTracking()
            .Where(snapshot => bindingIds.Contains(snapshot.ProviderSubjectDirectoryBindingId))
            .ToListAsync(cancellationToken);
        var localRecoveryEmail = await _dbContext.RecoveryEmails.AsNoTracking()
            .SingleOrDefaultAsync(record => record.LocalAccountId == localAccountId, cancellationToken);
        var sourceBootstrapRevoked = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == localAccountId)
            .Select(user => user.RecoverySourceBootstrapRevokedAtUtc != null)
            .SingleOrDefaultAsync(cancellationToken);

        // Metadata owns email evidence only. With no separate affiliation owner,
        // it cannot grant an exemption from the configured verification period.
        const bool requiresVerification = true;
        var recoveryEmail = EvaluateRecoveryEmail(localRecoveryEmail, snapshots, sourceBootstrapRevoked);
        var now = _timeProvider.GetUtcNow();
        var hasCurrentPeriodVerification =
            localRecoveryEmail?.VerifiedAtUtc is { } verifiedAt &&
            verifiedAt >= _options.EffectiveAtUtc!.Value;
        var bootstrapActive =
            _options.BootstrapEnabled &&
            now < _options.BootstrapUntilUtc!.Value &&
            recoveryEmail.HasAcceptedSourceTrustForAddress;
        var isWithinGracePeriod = now < _options.GraceEndsAtUtc!.Value;

        return new RecoveryVerificationPolicyDecision(
            true,
            _options.CurrentPeriodId,
            requiresVerification,
            hasCurrentPeriodVerification,
            bootstrapActive,
            isWithinGracePeriod,
            GetPeriodDisposition(
                now,
                requiresVerification,
                hasCurrentPeriodVerification,
                bootstrapActive,
                isWithinGracePeriod),
            recoveryEmail)
        {
            MetadataEvidenceStates = snapshots.Count == 0
                ? [ProviderMetadataEvidenceState.Missing]
                : snapshots.Select(GetEvidenceState).Distinct().ToArray()
        };
    }

    private RecoveryEmailPolicyDecision EvaluateRecoveryEmail(
        RecoveryEmailRecord? localRecoveryEmail,
        IReadOnlyCollection<ProviderMetadataSnapshot> snapshots,
        bool sourceBootstrapRevoked)
    {
        var sourceCandidates = sourceBootstrapRevoked
            ? Array.Empty<SourceEmailCandidate>()
            : snapshots
                .Select(TryCreateSourceCandidate)
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .GroupBy(candidate => candidate.NormalizedAddress, StringComparer.Ordinal)
                .Select(group => group.OrderBy(candidate => candidate.TrustOrigin).First())
                .OrderBy(candidate => candidate.NormalizedAddress, StringComparer.Ordinal)
                .ToArray();

        if (localRecoveryEmail is not null)
        {
            if (!RecoveryProofSecurity.TryNormalizeAddress(
                    localRecoveryEmail.Address,
                    out _,
                    out var localNormalizedAddress))
            {
                return new RecoveryEmailPolicyDecision(
                    null,
                    RecoveryEmailAddressSource.LocalRecord,
                    RecoveryEmailPolicyTrustOrigin.Unknown,
                    false,
                    sourceCandidates.Length > 0,
                    false);
            }

            var matchingSource = sourceCandidates.SingleOrDefault(
                candidate => string.Equals(candidate.NormalizedAddress, localNormalizedAddress, StringComparison.Ordinal));
            var hasSourceConflict = sourceCandidates.Any(
                candidate => !string.Equals(candidate.NormalizedAddress, localNormalizedAddress, StringComparison.Ordinal));
            var isLocallyVerified = localRecoveryEmail.VerifiedAtUtc is not null;

            return new RecoveryEmailPolicyDecision(
                localRecoveryEmail.Address,
                RecoveryEmailAddressSource.LocalRecord,
                isLocallyVerified
                    ? RecoveryEmailPolicyTrustOrigin.LocallyVerified
                    : matchingSource?.TrustOrigin ?? RecoveryEmailPolicyTrustOrigin.Unknown,
                isLocallyVerified || matchingSource is not null,
                hasSourceConflict,
                matchingSource is not null);
        }

        if (sourceCandidates.Length != 1)
        {
            return new RecoveryEmailPolicyDecision(
                null,
                RecoveryEmailAddressSource.None,
                RecoveryEmailPolicyTrustOrigin.Unknown,
                false,
                sourceCandidates.Length > 1,
                false);
        }

        var sourceCandidate = sourceCandidates[0];
        return new RecoveryEmailPolicyDecision(
            sourceCandidate.Address,
            RecoveryEmailAddressSource.ProviderSnapshot,
            sourceCandidate.TrustOrigin,
            true,
            false,
            true);
    }

    private SourceEmailCandidate? TryCreateSourceCandidate(ProviderMetadataSnapshot snapshot)
    {
        if (GetEvidenceState(snapshot) != ProviderMetadataEvidenceState.Available)
        {
            return null;
        }

        var acceptedOrigin = snapshot.EmailTrustOrigin switch
        {
            ProviderEmailTrustOrigin.SourceVerified when _options.AcceptSourceVerifiedEmails =>
                RecoveryEmailPolicyTrustOrigin.SourceVerified,
            ProviderEmailTrustOrigin.PolicyTrusted when _options.AcceptPolicyTrustedEmails =>
                RecoveryEmailPolicyTrustOrigin.PolicyTrusted,
            _ => RecoveryEmailPolicyTrustOrigin.Unknown
        };
        if (acceptedOrigin == RecoveryEmailPolicyTrustOrigin.Unknown ||
            !RecoveryProofSecurity.TryNormalizeAddress(snapshot.Email ?? string.Empty, out var address, out var normalizedAddress))
        {
            return null;
        }

        return new SourceEmailCandidate(address, normalizedAddress, acceptedOrigin);
    }

    private ProviderMetadataEvidenceState GetEvidenceState(ProviderMetadataSnapshot snapshot)
    {
        if (snapshot.EvidenceState != ProviderMetadataEvidenceState.Available)
        {
            return Enum.IsDefined(snapshot.EvidenceState)
                ? snapshot.EvidenceState
                : ProviderMetadataEvidenceState.Untrusted;
        }

        var now = _timeProvider.GetUtcNow();
        if (snapshot.RefreshedAtUtc > now || snapshot.VerifiedAt > now)
        {
            return ProviderMetadataEvidenceState.Untrusted;
        }

        return snapshot.RefreshedAtUtc < _options.EffectiveAtUtc!.Value
            ? ProviderMetadataEvidenceState.Stale
            : ProviderMetadataEvidenceState.Available;
    }

    private RecoveryPeriodDisposition GetPeriodDisposition(
        DateTimeOffset now,
        bool requiresVerification,
        bool hasCurrentPeriodVerification,
        bool bootstrapActive,
        bool isWithinGracePeriod)
    {
        if (!requiresVerification)
        {
            return RecoveryPeriodDisposition.NotRequired;
        }

        if (now < _options.EffectiveAtUtc)
        {
            return RecoveryPeriodDisposition.NotEffective;
        }

        if (hasCurrentPeriodVerification)
        {
            return RecoveryPeriodDisposition.Satisfied;
        }

        if (bootstrapActive)
        {
            return RecoveryPeriodDisposition.DeferredByBootstrap;
        }

        return isWithinGracePeriod
            ? RecoveryPeriodDisposition.DeferredByGrace
            : RecoveryPeriodDisposition.Required;
    }

    private static RecoveryVerificationPolicyDecision DisabledDecision() =>
        new(
            false,
            null,
            false,
            false,
            false,
            false,
            RecoveryPeriodDisposition.Disabled,
            new RecoveryEmailPolicyDecision(
                null,
                RecoveryEmailAddressSource.None,
                RecoveryEmailPolicyTrustOrigin.Unknown,
                false,
                false,
                false));

    private sealed record SourceEmailCandidate(
        string Address,
        string NormalizedAddress,
        RecoveryEmailPolicyTrustOrigin TrustOrigin);
}
