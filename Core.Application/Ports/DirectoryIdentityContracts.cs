using Core.Application.DTOs;

namespace Core.Application.Ports;

public interface IDirectoryIdentityLookup
{
    Task<DirectoryLookupResult> FindManagedIdentityAsync(
        string canonicalAccount,
        CancellationToken cancellationToken = default);
}

public interface IDirectoryCredentialAuthenticator
{
    Task<DirectoryCredentialResult> AuthenticateAsync(
        Guid directoryObjectId,
        string password,
        CancellationToken cancellationToken = default);
}

public interface IDirectoryCredentialResetter
{
    Task<DirectoryCredentialOperationResult> ResetCredentialAsync(
        Guid directoryObjectId,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public interface IDirectoryCredentialVerifier
{
    Task<DirectoryCredentialVerificationResult> VerifyCredentialAsync(
        Guid directoryObjectId,
        string password,
        CancellationToken cancellationToken = default);
}

public interface IDirectoryTemporaryCredentialCapability
{
    Task<DirectoryCredentialOperationResult> IssueTemporaryCredentialAsync(
        Guid directoryObjectId,
        string temporaryPassword,
        CancellationToken cancellationToken = default);

    Task<DirectoryCredentialOperationResult> ChangeRequiredCredentialAsync(
        Guid directoryObjectId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public sealed record DirectoryLookupResult(
    DirectoryLookupOutcome Outcome,
    ManagedDirectoryIdentity? Identity = null);

public sealed record ManagedDirectoryIdentity(
    Guid ObjectId,
    string CanonicalAccount,
    bool IsEligible,
    bool IsEnabled,
    bool IsLocked,
    AssuredProfile? Profile = null);

public sealed record DirectoryCredentialResult(
    DirectoryCredentialOutcome Outcome,
    ManagedDirectoryIdentity? Identity = null);

/// <summary>
/// A fresh password bind plus a separately-read directory identity. The identity is required
/// so a caller can verify the immutable object, managed eligibility, enabled, and unlocked gates.
/// </summary>
public sealed record DirectoryCredentialVerificationResult(
    DirectoryCredentialOutcome Outcome,
    ManagedDirectoryIdentity? Identity = null);

public sealed record DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome Outcome);

public enum DirectoryLookupOutcome
{
    Found,
    NotFound,
    Ambiguous,
    Ineligible,
    Malformed,
    Unavailable,
    Timeout
}

public enum DirectoryCredentialOutcome
{
    Authenticated,
    PasswordChangeRequired,
    InvalidCredentials,
    Disabled,
    Locked,
    Ineligible,
    Malformed,
    Unavailable,
    Timeout
}

public enum DirectoryCredentialOperationOutcome
{
    Succeeded,
    Disabled,
    Locked,
    Ineligible,
    Unsupported,
    Rejected,
    Malformed,
    Unavailable,
    Timeout
}
