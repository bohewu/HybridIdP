using Core.Application.Ports;
using Infrastructure.Options;

namespace Infrastructure.Directory;

/// <summary>
/// Deployment-provided protected directory read transport. It deliberately has no write operation.
/// </summary>
public interface IProtectedDirectoryIdentityTransport
{
    Task<ProtectedDirectoryTransportResult> FindExactAsync(
        string canonicalAccount,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Deployment-provided protected directory credential transport. Each call represents one
/// operation; callers deliberately do not retry write operations.
/// </summary>
public interface IProtectedDirectoryCredentialTransport
{
    Task<ProtectedDirectoryCredentialTransportResult> AuthenticateAsync(
        Guid directoryObjectId,
        string password,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default);

    Task<ProtectedDirectoryCredentialOperationTransportResult> ResetAsync(
        Guid directoryObjectId,
        string newPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default);

    Task<ProtectedDirectoryCredentialTransportResult> VerifyAsync(
        Guid directoryObjectId,
        string password,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default);

    Task<ProtectedDirectoryCredentialOperationTransportResult> IssueTemporaryAsync(
        Guid directoryObjectId,
        string temporaryPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default);

    Task<ProtectedDirectoryCredentialOperationTransportResult> ChangeRequiredAsync(
        Guid directoryObjectId,
        string currentPassword,
        string newPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default);
}

public sealed record ProtectedDirectoryTransportResult(
    ProtectedDirectoryTransportOutcome Outcome,
    IReadOnlyList<ManagedDirectoryIdentity> Identities)
{
    public static ProtectedDirectoryTransportResult Unavailable() =>
        new(ProtectedDirectoryTransportOutcome.Unavailable, []);
}

public sealed record ProtectedDirectoryCredentialTransportResult(
    ProtectedDirectoryCredentialTransportOutcome Outcome,
    ManagedDirectoryIdentity? Identity = null)
{
    public static ProtectedDirectoryCredentialTransportResult Unavailable() =>
        new(ProtectedDirectoryCredentialTransportOutcome.Unavailable);
}

public sealed record ProtectedDirectoryCredentialOperationTransportResult(
    ProtectedDirectoryCredentialOperationTransportOutcome Outcome)
{
    public static ProtectedDirectoryCredentialOperationTransportResult Unavailable() =>
        new(ProtectedDirectoryCredentialOperationTransportOutcome.Unavailable);
}

public enum ProtectedDirectoryTransportOutcome
{
    Succeeded,
    Malformed,
    Unavailable,
    Timeout
}

public enum ProtectedDirectoryCredentialTransportOutcome
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

public enum ProtectedDirectoryCredentialOperationTransportOutcome
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

/// <summary>
/// Safe default used until a deployment supplies its protected LDAPS, StartTLS, or Negotiate transport.
/// </summary>
public sealed class UnavailableProtectedDirectoryIdentityTransport :
    IProtectedDirectoryIdentityTransport,
    IProtectedDirectoryCredentialTransport
{
    public Task<ProtectedDirectoryTransportResult> FindExactAsync(
        string canonicalAccount,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtectedDirectoryTransportResult.Unavailable());

    public Task<ProtectedDirectoryCredentialTransportResult> AuthenticateAsync(
        Guid directoryObjectId,
        string password,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtectedDirectoryCredentialTransportResult.Unavailable());

    public Task<ProtectedDirectoryCredentialOperationTransportResult> ResetAsync(
        Guid directoryObjectId,
        string newPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtectedDirectoryCredentialOperationTransportResult.Unavailable());

    public Task<ProtectedDirectoryCredentialTransportResult> VerifyAsync(
        Guid directoryObjectId,
        string password,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtectedDirectoryCredentialTransportResult.Unavailable());

    public Task<ProtectedDirectoryCredentialOperationTransportResult> IssueTemporaryAsync(
        Guid directoryObjectId,
        string temporaryPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtectedDirectoryCredentialOperationTransportResult.Unavailable());

    public Task<ProtectedDirectoryCredentialOperationTransportResult> ChangeRequiredAsync(
        Guid directoryObjectId,
        string currentPassword,
        string newPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtectedDirectoryCredentialOperationTransportResult.Unavailable());
}
