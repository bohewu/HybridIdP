using Core.Domain.Entities;

namespace Core.Application.Ports;

public interface IRecoveryVerificationPolicyEvaluator
{
    Task<RecoveryVerificationPolicyDecision> EvaluateAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default);
}

public sealed record RecoveryVerificationPolicyDecision(
    bool Enabled,
    string? CurrentPeriodId,
    bool RequiresCurrentPeriodVerification,
    bool HasCurrentPeriodVerification,
    bool BootstrapActive,
    bool IsWithinGracePeriod,
    RecoveryPeriodDisposition PeriodDisposition,
    RecoveryEmailPolicyDecision RecoveryEmail)
{
    public IReadOnlyList<ProviderMetadataEvidenceState> MetadataEvidenceStates { get; init; } =
        [ProviderMetadataEvidenceState.Missing];
}

public sealed record RecoveryEmailPolicyDecision(
    string? Address,
    RecoveryEmailAddressSource AddressSource,
    RecoveryEmailPolicyTrustOrigin TrustOrigin,
    bool CanReceiveRecoveryOtp,
    bool HasSourceConflict,
    bool HasAcceptedSourceTrustForAddress);

public enum RecoveryPeriodDisposition
{
    Disabled,
    NotRequired,
    NotEffective,
    Satisfied,
    DeferredByBootstrap,
    DeferredByGrace,
    Required
}

public enum RecoveryEmailAddressSource
{
    None,
    LocalRecord,
    ProviderSnapshot
}

public enum RecoveryEmailPolicyTrustOrigin
{
    Unknown,
    LocallyVerified,
    SourceVerified,
    PolicyTrusted
}
