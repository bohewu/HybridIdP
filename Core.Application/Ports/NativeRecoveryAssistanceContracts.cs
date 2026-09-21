namespace Core.Application.Ports;

public interface INativeRecoveryAssistanceService
{
    Task<NativeRecoveryAssistanceResult> ResendAsync(
        AdminNativeRecoveryResendRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryEmailChangeResult> ReplaceEmailAsync(
        AdminNativeRecoveryEmailReplacementRequest request,
        CancellationToken cancellationToken = default);
    Task<ResetApprovalIssueResult> ApproveResetAsync(
        AdminNativeRecoveryApprovalRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> VerifyReplacementAsync(
        NativeRecoveryReplacementVerificationRequest request,
        CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> GetApprovalStatusAsync(
        NativeRecoveryApprovalStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AdminNativeRecoveryResendRequest(Guid ActorAccountId, Guid TargetAccountId);
public sealed record AdminNativeRecoveryEmailReplacementRequest(
    Guid ActorAccountId,
    Guid TargetAccountId,
    string CandidateAddress,
    string IdentityCheckEvidence,
    string Reason);
public sealed record AdminNativeRecoveryApprovalRequest(
    Guid ActorAccountId,
    Guid TargetAccountId,
    string IdentityCheckEvidence,
    string Reason);
public sealed record NativeRecoveryReplacementVerificationRequest(
    Guid RequestId,
    string Code,
    NativeRecoveryContext Context);
public sealed record NativeRecoveryApprovalStatusRequest(Guid RequestId, NativeRecoveryContext Context);
public sealed record NativeRecoveryAssistanceResult(RecoveryProofOutcome Outcome, int RetryAfterSeconds = 0);
