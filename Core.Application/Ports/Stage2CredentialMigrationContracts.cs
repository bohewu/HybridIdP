using Core.Application.DTOs;

namespace Core.Application.Ports;

/// <summary>
/// Coordinates the credential-migration ceremony. Results are intentionally non-enumerating.
/// </summary>
public interface IStage2CredentialMigrationService
{
    Task<MigrationProofCeremonyResult> BeginAsync(
        MigrationProofCeremonyRequest request,
        CancellationToken cancellationToken = default);

    Task<MigrationCommitCeremonyResult> CommitAsync(
        MigrationCommitCeremonyRequest request,
        CancellationToken cancellationToken = default);

    Task<DirectoryCredentialResult> AuthenticateCompletedAsync(
        Guid localAccountId,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record MigrationProofCeremonyRequest(
    string AccountName,
    string Password,
    MigrationContinuationContext Context,
    EmailOtpPolicy RequestedEmailOtpPolicy = EmailOtpPolicy.Disabled);

public sealed record MigrationCommitCeremonyRequest(
    string Continuation,
    string NewPassword,
    MigrationContinuationContext Context,
    string? MigrationOtpProof = null);

public sealed record MigrationProofCeremonyResult(
    MigrationCeremonyOutcome Outcome,
    bool RequiresEmailOtp = false,
    string? Continuation = null);

public sealed record MigrationCommitCeremonyResult(
    MigrationCeremonyOutcome Outcome);

public enum MigrationCeremonyOutcome
{
    ContinuationIssued,
    Completed,
    Denied,
    Unavailable
}
