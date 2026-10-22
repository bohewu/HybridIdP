namespace Core.Application.Ports;

public interface IProviderMetadataRefreshService
{
    Task<ProviderMetadataRefreshOutcome> RefreshAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProviderMetadataRefreshOutcome.Legacy);
    Task<ProviderMetadataRefreshOutcome> RefreshAsync(
        string providerNamespace,
        string stableSubject,
        CancellationToken cancellationToken = default);
}

public enum ProviderMetadataRefreshOutcome
{
    Refreshed,
    Disabled,
    InvalidRequest,
    BindingNotFound,
    Unavailable,
    Malformed,
    Missing,
    Unsupported,
    Untrusted,
    AuthenticationFailed,
    TimedOut,
    Legacy
}
