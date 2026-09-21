using Core.Domain.Entities;

namespace Core.Application.Ports;

public interface INativeDirectoryRecoveryBarrier
{
    Task<bool> HasIssuanceBarrierAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default);
}

public interface INativeDirectoryRecoveryReconciliationService
{
    Task<NativeDirectoryRecoveryReconciliationOutcome> ReconcileAsync(
        Guid actorAccountId,
        Guid localAccountId,
        string intendedPassword,
        CancellationToken cancellationToken = default);
}

public interface IDirectoryCredentialOperatorResolutionService
{
    Task<DirectoryCredentialOperatorResolutionLookupResult> GetPendingAsync(
        Guid actorAccountId,
        Guid localAccountId,
        CancellationToken cancellationToken = default);

    Task<DirectorySettlementPreparationResult> PrepareAsync(
        DirectorySettlementPreparationRequest request,
        CancellationToken cancellationToken = default);

    Task<DirectorySettlementClaimResult> ClaimAsync(
        DirectorySettlementClaimRequest request,
        CancellationToken cancellationToken = default);

    Task<DirectorySettlementVerificationOutcome> VerifyAndFinalizeAsync(
        DirectorySettlementVerificationRequest request,
        CancellationToken cancellationToken = default);

    Task<DirectoryCredentialOperatorResolutionOutcome> CancelAsync(
        Guid actorAccountId,
        Guid preparationId,
        CancellationToken cancellationToken = default);

    Task<DirectorySettlementVerificationOutcome> CancelUserAsync(
        Guid preparationId,
        NativeRecoveryContext context,
        CancellationToken cancellationToken = default);
}

public sealed record DirectorySettlementPreparationRequest(
    Guid ActorAccountId,
    Guid LocalAccountId,
    Guid AttemptId,
    long ExpectedVersion,
    NativeDirectoryCredentialOperationKind OperationKind,
    NativeDirectoryRecoveryStatus ExpectedStatus,
    Guid DirectoryObjectId,
    bool OriginalWritersDrained,
    DirectorySettlementDisposition Disposition,
    DirectorySettlementEvidenceCategory EvidenceCategory,
    string EvidenceReference);

public sealed record DirectorySettlementPreparationResult(
    DirectoryCredentialOperatorResolutionOutcome Outcome,
    Guid? PreparationId = null);

public sealed record DirectorySettlementClaimRequest(
    string Continuation,
    NativeRecoveryContext Context);

public sealed record DirectorySettlementClaimResult(
    DirectorySettlementVerificationOutcome Outcome,
    Guid? PreparationId = null);

public sealed record DirectorySettlementVerificationRequest(
    Guid PreparationId,
    string OwnershipCode,
    string CurrentCredential,
    NativeRecoveryContext Context);

public enum DirectorySettlementVerificationOutcome
{
    ChallengeIssued,
    Resolved,
    Unavailable
}

public sealed record DirectoryCredentialOperatorResolutionAttempt(
    Guid AttemptId,
    long Version,
    NativeDirectoryCredentialOperationKind OperationKind,
    NativeDirectoryRecoveryStatus Status,
    Guid DirectoryObjectId);

public sealed record DirectoryCredentialOperatorResolutionLookupResult(
    DirectoryCredentialOperatorResolutionOutcome Outcome,
    DirectoryCredentialOperatorResolutionAttempt? Attempt = null);

public enum DirectoryCredentialOperatorResolutionOutcome
{
    Resolved,
    Available,
    Unavailable,
    Unauthorized
}

public enum NativeDirectoryRecoveryReconciliationOutcome
{
    Reconciled,
    Unresolved,
    Unauthorized
}
