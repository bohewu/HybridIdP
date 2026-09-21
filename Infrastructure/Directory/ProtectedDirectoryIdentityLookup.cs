using Core.Application.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Directory;

/// <summary>
/// Applies the exact-one and managed-account gates to a protected read-only directory transport.
/// </summary>
public sealed class ProtectedDirectoryIdentityLookup : IDirectoryIdentityLookup
{
    private readonly IProtectedDirectoryIdentityTransport _transport;
    private readonly DirectoryIntegrationOptions _directoryOptions;
    private readonly DirectoryLookupOptions _lookupOptions;

    public ProtectedDirectoryIdentityLookup(
        IProtectedDirectoryIdentityTransport transport,
        IOptions<DirectoryIntegrationOptions> directoryOptions,
        IOptions<DirectoryLookupOptions> lookupOptions)
    {
        _transport = transport;
        _directoryOptions = directoryOptions.Value;
        _lookupOptions = lookupOptions.Value;
    }

    public async Task<DirectoryLookupResult> FindManagedIdentityAsync(
        string canonicalAccount,
        CancellationToken cancellationToken = default)
    {
        if (!_directoryOptions.Enabled)
        {
            return new DirectoryLookupResult(DirectoryLookupOutcome.Unavailable);
        }

        if (string.IsNullOrWhiteSpace(canonicalAccount))
        {
            return new DirectoryLookupResult(DirectoryLookupOutcome.Malformed);
        }

        try
        {
            using var timeout = new CancellationTokenSource(_lookupOptions.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var result = await _transport.FindExactAsync(
                canonicalAccount,
                _directoryOptions.Transport,
                linked.Token);

            if (result.Outcome != ProtectedDirectoryTransportOutcome.Succeeded)
            {
                return new DirectoryLookupResult(result.Outcome switch
                {
                    ProtectedDirectoryTransportOutcome.Timeout => DirectoryLookupOutcome.Timeout,
                    ProtectedDirectoryTransportOutcome.Malformed => DirectoryLookupOutcome.Malformed,
                    _ => DirectoryLookupOutcome.Unavailable
                });
            }

            if (result.Identities is null || result.Identities.Count == 0)
            {
                return new DirectoryLookupResult(DirectoryLookupOutcome.NotFound);
            }

            if (result.Identities.Count != 1)
            {
                return new DirectoryLookupResult(DirectoryLookupOutcome.Ambiguous);
            }

            var identity = result.Identities[0];
            if (identity.ObjectId == Guid.Empty ||
                !string.Equals(identity.CanonicalAccount, canonicalAccount, StringComparison.Ordinal) ||
                identity.Profile is { } profile && !profile.IsValid())
            {
                return new DirectoryLookupResult(DirectoryLookupOutcome.Malformed);
            }

            if (!identity.IsEligible || !identity.IsEnabled || identity.IsLocked)
            {
                return new DirectoryLookupResult(DirectoryLookupOutcome.Ineligible);
            }

            return new DirectoryLookupResult(DirectoryLookupOutcome.Found, identity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new DirectoryLookupResult(DirectoryLookupOutcome.Timeout);
        }
        catch
        {
            return new DirectoryLookupResult(DirectoryLookupOutcome.Unavailable);
        }
    }
}
