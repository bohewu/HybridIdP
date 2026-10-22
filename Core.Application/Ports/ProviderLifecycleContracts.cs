using Core.Application.DTOs;

namespace Core.Application.Ports;

public interface IProviderLifecycleClient
{
    Task<ProviderLifecycleLookupResult> LookupAsync(ProviderLifecycleBinding binding, CancellationToken cancellationToken = default);
    Task<NeutralQueryResponse?> LookupNeutralAsync(NeutralQueryContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult<NeutralQueryResponse?>(null);
}

public enum ProviderLifecycleFailure { None, Disabled, Unavailable, Timeout, Malformed, AuthenticationFailed }

public sealed record ProviderLifecycleLookupResult
{
    public ProviderLifecycleFailure Failure { get; init; }
    public ProviderLifecycleResponse? Response { get; init; }
    public override string ToString() => "ProviderLifecycleLookupResult [redacted]";
}
