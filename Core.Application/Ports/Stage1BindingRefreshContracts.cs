using Core.Application.DTOs;

namespace Core.Application.Ports;

/// <summary>
/// Persists a verified provider-to-directory binding and applies an allowlisted local profile refresh.
/// </summary>
public interface IStage1BindingRefreshService
{
    Task<Stage1BindingRefreshOutcome> BindAndRefreshAsync(
        Stage1BindingRefreshRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record Stage1BindingRefreshRequest(
    Guid LocalAccountId,
    string ProviderNamespace,
    string StableSubject,
    ManagedDirectoryIdentity DirectoryIdentity);

public enum Stage1BindingRefreshOutcome
{
    BoundAndRefreshed,
    ExistingBindingRefreshed,
    Conflict,
    Invalid
}
