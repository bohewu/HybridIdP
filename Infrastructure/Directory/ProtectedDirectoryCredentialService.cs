using Core.Application.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Directory;

/// <summary>
/// Maps deployment-selected protected directory operations to the application ports.
/// It contains no retry loop, especially for password reset.
/// </summary>
public sealed class ProtectedDirectoryCredentialService :
    IDirectoryCredentialAuthenticator,
    IDirectoryCredentialResetter,
    IDirectoryCredentialVerifier,
    IDirectoryTemporaryCredentialCapability
{
    private readonly IProtectedDirectoryCredentialTransport _transport;
    private readonly DirectoryIntegrationOptions _options;
    private readonly DirectoryLookupOptions _lookupOptions;

    public ProtectedDirectoryCredentialService(
        IProtectedDirectoryCredentialTransport transport,
        IOptions<DirectoryIntegrationOptions> options,
        IOptions<DirectoryLookupOptions> lookupOptions)
    {
        _transport = transport;
        _options = options.Value;
        _lookupOptions = lookupOptions.Value;
    }

    public async Task<DirectoryCredentialResult> AuthenticateAsync(
        Guid directoryObjectId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            (transport, token) => transport.AuthenticateAsync(directoryObjectId, password, _options.Transport, token),
            cancellationToken);
        return new DirectoryCredentialResult(Map(result.Outcome), result.Identity);
    }

    public async Task<DirectoryCredentialOperationResult> ResetCredentialAsync(
        Guid directoryObjectId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || directoryObjectId == Guid.Empty || string.IsNullOrEmpty(newPassword))
        {
            return new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Malformed);
        }

        try
        {
            using var timeout = new CancellationTokenSource(_lookupOptions.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var result = await _transport.ResetAsync(directoryObjectId, newPassword, _options.Transport, linked.Token);
            return new DirectoryCredentialOperationResult(Map(result.Outcome));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Timeout);
        }
        catch
        {
            return new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Unavailable);
        }
    }

    public async Task<DirectoryCredentialVerificationResult> VerifyCredentialAsync(
        Guid directoryObjectId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            (transport, token) => transport.VerifyAsync(directoryObjectId, password, _options.Transport, token),
            cancellationToken);
        return new DirectoryCredentialVerificationResult(Map(result.Outcome), result.Identity);
    }

    public Task<DirectoryCredentialOperationResult> IssueTemporaryCredentialAsync(
        Guid directoryObjectId,
        string temporaryPassword,
        CancellationToken cancellationToken = default) =>
        ExecuteTemporaryOperationAsync(
            (transport, token) => transport.IssueTemporaryAsync(
                directoryObjectId,
                temporaryPassword,
                _options.Transport,
                token),
            cancellationToken);

    public Task<DirectoryCredentialOperationResult> ChangeRequiredCredentialAsync(
        Guid directoryObjectId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        ExecuteTemporaryOperationAsync(
            (transport, token) => transport.ChangeRequiredAsync(
                directoryObjectId,
                currentPassword,
                newPassword,
                _options.Transport,
                token),
            cancellationToken);

    private async Task<DirectoryCredentialOperationResult> ExecuteTemporaryOperationAsync(
        Func<IProtectedDirectoryCredentialTransport, CancellationToken,
            Task<ProtectedDirectoryCredentialOperationTransportResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.TemporaryCredentialCapabilityEnabled)
        {
            return new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Unsupported);
        }

        try
        {
            using var timeout = new CancellationTokenSource(_lookupOptions.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            return new DirectoryCredentialOperationResult(Map((await operation(_transport, linked.Token)).Outcome));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Timeout);
        }
        catch
        {
            return new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Unavailable);
        }
    }

    private async Task<ProtectedDirectoryCredentialTransportResult> ExecuteAsync(
        Func<IProtectedDirectoryCredentialTransport, CancellationToken, Task<ProtectedDirectoryCredentialTransportResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ProtectedDirectoryCredentialTransportResult.Unavailable();
        }

        try
        {
            using var timeout = new CancellationTokenSource(_lookupOptions.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            return await operation(_transport, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ProtectedDirectoryCredentialTransportResult(ProtectedDirectoryCredentialTransportOutcome.Timeout);
        }
        catch
        {
            return ProtectedDirectoryCredentialTransportResult.Unavailable();
        }
    }

    private static DirectoryCredentialOutcome Map(ProtectedDirectoryCredentialTransportOutcome outcome) => outcome switch
    {
        ProtectedDirectoryCredentialTransportOutcome.Authenticated => DirectoryCredentialOutcome.Authenticated,
        ProtectedDirectoryCredentialTransportOutcome.PasswordChangeRequired => DirectoryCredentialOutcome.PasswordChangeRequired,
        ProtectedDirectoryCredentialTransportOutcome.InvalidCredentials => DirectoryCredentialOutcome.InvalidCredentials,
        ProtectedDirectoryCredentialTransportOutcome.Disabled => DirectoryCredentialOutcome.Disabled,
        ProtectedDirectoryCredentialTransportOutcome.Locked => DirectoryCredentialOutcome.Locked,
        ProtectedDirectoryCredentialTransportOutcome.Ineligible => DirectoryCredentialOutcome.Ineligible,
        ProtectedDirectoryCredentialTransportOutcome.Malformed => DirectoryCredentialOutcome.Malformed,
        ProtectedDirectoryCredentialTransportOutcome.Timeout => DirectoryCredentialOutcome.Timeout,
        _ => DirectoryCredentialOutcome.Unavailable
    };

    private static DirectoryCredentialOperationOutcome Map(ProtectedDirectoryCredentialOperationTransportOutcome outcome) => outcome switch
    {
        ProtectedDirectoryCredentialOperationTransportOutcome.Succeeded => DirectoryCredentialOperationOutcome.Succeeded,
        ProtectedDirectoryCredentialOperationTransportOutcome.Disabled => DirectoryCredentialOperationOutcome.Disabled,
        ProtectedDirectoryCredentialOperationTransportOutcome.Locked => DirectoryCredentialOperationOutcome.Locked,
        ProtectedDirectoryCredentialOperationTransportOutcome.Ineligible => DirectoryCredentialOperationOutcome.Ineligible,
        ProtectedDirectoryCredentialOperationTransportOutcome.Unsupported => DirectoryCredentialOperationOutcome.Unsupported,
        ProtectedDirectoryCredentialOperationTransportOutcome.Rejected => DirectoryCredentialOperationOutcome.Rejected,
        ProtectedDirectoryCredentialOperationTransportOutcome.Malformed => DirectoryCredentialOperationOutcome.Malformed,
        ProtectedDirectoryCredentialOperationTransportOutcome.Timeout => DirectoryCredentialOperationOutcome.Timeout,
        _ => DirectoryCredentialOperationOutcome.Unavailable
    };
}
