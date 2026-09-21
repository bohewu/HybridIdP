namespace Core.Application.Ports;

public interface IRecoveryAssistanceContextService
{
    Task<RecoveryAssistanceContextResult> GetAsync(
        Guid actorAccountId,
        Guid targetAccountId,
        CancellationToken cancellationToken = default);
}

public sealed record RecoveryAssistanceContextResult(
    RecoveryAssistanceContextOutcome Outcome,
    RecoveryAssistanceContextDto? Context = null);

public enum RecoveryAssistanceContextOutcome
{
    Available,
    Unauthorized
}

public sealed record RecoveryAssistanceContextDto(
    OrdinaryRecoveryAvailabilityDto Ordinary,
    TemporaryCredentialAvailabilityDto Temporary,
    MigrationRecoveryAvailabilityDto Migration,
    PendingDirectoryAvailabilityDto Pending);

public sealed record OrdinaryRecoveryAvailabilityDto(
    string State,
    bool ResendOtp,
    bool ReplaceRecoveryEmail,
    bool ApproveReset);

public sealed record TemporaryCredentialAvailabilityDto(
    string State,
    bool IssueTemporaryCredential);

public sealed record MigrationRecoveryAvailabilityDto(
    string State,
    bool ResendOtp,
    bool ReplaceRecoveryEmail,
    bool ApproveReset);

public sealed record PendingDirectoryAvailabilityDto(
    string State,
    bool Inspect,
    bool PrepareSettlement);
