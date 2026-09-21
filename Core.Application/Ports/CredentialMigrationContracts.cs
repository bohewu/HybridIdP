using Core.Application.DTOs;
using Core.Domain.Entities;

namespace Core.Application.Ports;

public interface ICredentialMigrationPolicy
{
    MigrationPolicyDecision Evaluate(MigrationPolicyRequest request, DateTimeOffset now);
}

public interface ICredentialMigrationStateStore
{
    Task<CredentialMigrationRecord?> FindAsync(Guid localAccountId, CancellationToken cancellationToken = default);

    Task<CredentialMigrationRecord> EnsureRequiredAsync(
        Guid localAccountId,
        DirectoryObjectBinding binding,
        CancellationToken cancellationToken = default);

    Task<CredentialMigrationRecord> AdvanceAsync(
        Guid localAccountId,
        CredentialMigrationState expectedState,
        CredentialMigrationState nextState,
        CancellationToken cancellationToken = default);

    Task<CredentialMigrationRecord> AdvanceToProofValidatedAsync(
        Guid localAccountId,
        EffectiveEmailOtpRequirement effectiveEmailOtpRequirement,
        CancellationToken cancellationToken = default);
}

public interface IMigrationContinuationStore
{
    Task<MigrationContinuation> CreateAsync(
        MigrationContinuationRequest request,
        CancellationToken cancellationToken = default);

    Task<MigrationContinuationConsumption> ConsumeAsync(
        string protectedValue,
        MigrationContinuationContext context,
        CancellationToken cancellationToken = default);

    Task<MigrationContinuationConsumption> InspectAsync(
        string protectedValue,
        MigrationContinuationContext context,
        CancellationToken cancellationToken = default);
}

public interface ICredentialMigrationRecoveryService
{
    Task<MigrationRecoveryContinuationResult> BeginDirectoryRecoveryAsync(
        MigrationRecoveryBeginRequest request,
        CancellationToken cancellationToken = default);

    Task<MigrationRecoveryResult> RecoverAsync(
        MigrationRecoveryRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMigrationRecoveryAuthorizer
{
    Task<bool> IsAuthorizedAsync(CancellationToken cancellationToken = default);
}

public interface IMigrationRecoveryEvidenceReader
{
    Task<MigrationCommittedEvidence> ReadDirectoryCommitmentAsync(
        CredentialMigrationRecord record,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<MigrationCommittedEvidence> ReadLocalFinalizationAsync(
        CredentialMigrationRecord record,
        CancellationToken cancellationToken = default);
}

public sealed record MigrationPolicyRequest(
    Guid LocalAccountId,
    bool IsCompleted,
    EmailOtpPolicy RequestedEmailOtpPolicy);

public sealed record MigrationPolicyDecision(
    bool AllowLegacyProof,
    EmailOtpPolicy EffectiveEmailOtpPolicy);

public sealed record DirectoryObjectBinding(
    string ProviderNamespace,
    string StableSubject,
    Guid DirectoryObjectId,
    string? CanonicalAccountAlias = null);

public sealed record CredentialMigrationRecord(
    Guid LocalAccountId,
    DirectoryObjectBinding Binding,
    CredentialMigrationState State,
    EffectiveEmailOtpRequirement EffectiveEmailOtpRequirement = EffectiveEmailOtpRequirement.Unspecified);

public sealed record MigrationContinuationRequest(
    Guid LocalAccountId,
    DirectoryObjectBinding Binding,
    MigrationContinuationContext Context,
    DateTimeOffset? ExpiresAt = null);

public sealed record MigrationContinuation(
    string ProtectedValue,
    DateTimeOffset ExpiresAt);

public sealed record MigrationContinuationContext(
    string ContextHash,
    string CsrfHash);

public sealed record MigrationContinuationConsumption(
    MigrationContinuationConsumptionOutcome Outcome,
    CredentialMigrationRecord? Record = null,
    Guid? SourceAttemptId = null,
    long? SourceAttemptVersion = null);

public sealed record MigrationRecoveryBeginRequest(
    Guid LocalAccountId,
    MigrationContinuationContext Context);

public sealed record MigrationRecoveryContinuationResult(
    MigrationRecoveryOutcome Outcome,
    string? Continuation = null);

public sealed record MigrationRecoveryRequest(
    Guid LocalAccountId,
    string? Continuation = null,
    string? NewPassword = null,
    MigrationContinuationContext? Context = null);

public sealed record MigrationRecoveryResult(MigrationRecoveryOutcome Outcome);

public enum MigrationContinuationConsumptionOutcome
{
    Consumed,
    Expired,
    AlreadyUsed,
    ContextMismatch,
    Unavailable
}

public enum MigrationRecoveryOutcome
{
    Reconciled,
    Unresolved,
    Unauthorized,
    Unavailable
}

public enum MigrationCommittedEvidence
{
    None,
    DirectoryCredentialCommitted,
    LocalFinalized
}
