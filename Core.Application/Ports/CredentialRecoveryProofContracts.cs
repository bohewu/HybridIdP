namespace Core.Application.Ports;

public interface IRecoveryEmailService
{
    Task<RecoveryEmailStatus> GetStatusAsync(Guid localAccountId, CancellationToken cancellationToken = default);
    Task<RecoveryEmailChangeResult> BeginAuthenticatedChangeAsync(
        RecoveryEmailChangeRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> VerifyAuthenticatedAsync(
        RecoveryEmailVerificationRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> VerifyForMigrationAsync(
        MigrationRecoveryEmailVerificationRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> RevokeAuthenticatedAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default);
}

public interface IMigrationOtpProofService
{
    Task<MigrationOtpSendResult> SendAsync(
        MigrationOtpSendRequest request,
        CancellationToken cancellationToken = default);
    Task<MigrationOtpVerificationResult> VerifyAsync(
        MigrationOtpVerificationRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> ConsumeAsync(
        MigrationOtpConsumptionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRecoveryAssistanceService
{
    Task<MigrationOtpSendResult> ResendMigrationOtpAsync(
        AdminMigrationOtpResendRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryEmailChangeResult> ReplaceRecoveryEmailAsync(
        AdminRecoveryEmailReplacementRequest request,
        CancellationToken cancellationToken = default);
    Task<ResetApprovalIssueResult> IssueResetApprovalAsync(
        AdminResetApprovalRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> GetResetApprovalStatusAsync(
        ResetApprovalConsumptionRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> ConsumeResetApprovalAsync(
        ResetApprovalConsumptionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRecoveryProofAuthorizer
{
    Task<bool> IsSelfServiceAuthorizedAsync(Guid localAccountId, CancellationToken cancellationToken = default);
    Task<bool> IsAdministratorAuthorizedAsync(Guid actorAccountId, CancellationToken cancellationToken = default);
}

public interface IRecoveryProofAudit
{
    Task RecordAsync(RecoveryProofAuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public interface IAdminTemporaryCredentialService
{
    Task<AdminTemporaryCredentialResult> IssueAsync(
        AdminTemporaryCredentialRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDirectoryRequiredCredentialChangeService
{
    Task<RecoveryProofOutcome> ChangeAsync(
        DirectoryRequiredCredentialChangeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record RecoveryEmailChangeRequest(Guid LocalAccountId, string CandidateAddress);
public sealed record RecoveryEmailVerificationRequest(Guid LocalAccountId, string Code);
public sealed record MigrationRecoveryEmailVerificationRequest(
    string Continuation,
    MigrationContinuationContext Context,
    string Code);
public sealed record MigrationOtpSendRequest(string Continuation, MigrationContinuationContext Context);
public sealed record MigrationOtpVerificationRequest(
    string Continuation,
    MigrationContinuationContext Context,
    string Code);
public sealed record MigrationOtpConsumptionRequest(
    string Continuation,
    MigrationContinuationContext Context,
    string Proof);
public sealed record AdminMigrationOtpResendRequest(
    Guid ActorAccountId,
    Guid TargetAccountId);
public sealed record AdminRecoveryEmailReplacementRequest(
    Guid ActorAccountId,
    Guid TargetAccountId,
    string CandidateAddress,
    string IdentityCheckEvidence,
    string Reason);
public sealed record AdminResetApprovalRequest(
    Guid ActorAccountId,
    Guid TargetAccountId,
    string IdentityCheckEvidence,
    string Reason);
public sealed record AdminTemporaryCredentialRequest(
    Guid ActorAccountId,
    Guid TargetAccountId,
    string IdentityCheckEvidence,
    string Reason);
public sealed record DirectoryRequiredCredentialChangeRequest(
    Guid LocalAccountId,
    Guid DirectoryObjectId,
    string CurrentPassword,
    string NewPassword);
public sealed record ResetApprovalConsumptionRequest(
    string Continuation,
    MigrationContinuationContext Context);

public sealed record RecoveryEmailStatus(bool IsConfigured, bool IsVerified, string? MaskedAddress = null);
public sealed record RecoveryEmailChangeResult(RecoveryProofOutcome Outcome);
public sealed record MigrationOtpSendResult(RecoveryProofOutcome Outcome, int RetryAfterSeconds = 0);
public sealed record MigrationOtpVerificationResult(RecoveryProofOutcome Outcome, string? Proof = null);
public sealed record ResetApprovalIssueResult(RecoveryProofOutcome Outcome);
public sealed record AdminTemporaryCredentialResult(
    RecoveryProofOutcome Outcome,
    string? TemporaryPassword = null);

public sealed record RecoveryProofAuditEvent(
    Guid CorrelationId,
    RecoveryProofAuditCategory Category,
    Guid TargetAccountId,
    Guid? ActorAccountId = null);

public enum RecoveryProofOutcome
{
    Success,
    Missing,
    Invalid,
    Expired,
    Exhausted,
    Replayed,
    Cooldown,
    Unauthorized,
    Unavailable
}

public enum RecoveryProofAuditCategory
{
    RecoveryAddressChangeStarted,
    RecoveryAddressVerified,
    RecoveryAddressRevoked,
    AdminRecoveryAddressReplaced,
    AdminMigrationOtpResent,
    AdminResetApprovalIssued,
    AdminResetApprovalConsumed,
    AdminTemporaryCredentialIssued,
    DirectoryCredentialOperatorResolved,
    AdminNativeRecoveryOtpResent,
    AdminNativeRecoveryAddressReplaced,
    AdminNativeResetApprovalIssued,
    NativeRecoveryAddressVerified
}
