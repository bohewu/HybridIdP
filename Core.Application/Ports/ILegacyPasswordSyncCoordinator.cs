using Core.Application.DTOs;

namespace Core.Application.Ports;

public interface ILegacyPasswordSyncCoordinator
{
    Task<LegacyPasswordSyncResult> SynchronizeAsync(
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncCohort cohort,
        Guid localAccountId,
        Guid providerSubjectDirectoryBindingId,
        string expectedAccountConcurrencyStamp,
        string expectedSecurityStamp,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record LegacyPasswordSyncResult(
    LegacyPasswordSyncResultOutcome Outcome,
    Guid? OperationId = null)
{
    public override string ToString() => $"{nameof(LegacyPasswordSyncResult)}:{Outcome}";
}

public enum LegacyPasswordSyncResultOutcome
{
    NotEligible,
    DuplicateSource,
    Blocked,
    Succeeded,
    Failed,
    Unknown
}

public interface ILegacyPasswordSyncTransport
{
    Task<LegacyPasswordSyncTransportResult> SendAsync(
        LegacyPasswordSyncRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record LegacyPasswordSyncTransportResult(
    LegacyPasswordSyncTransportOutcome Outcome,
    string? ResponseJson = null);

public enum LegacyPasswordSyncTransportOutcome
{
    PreDispatchRejected,
    TrustedResponse,
    PossibleDispatchFailure,
    UntrustedResponse
}
