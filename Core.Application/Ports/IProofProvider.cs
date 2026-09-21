using Core.Application.DTOs;

namespace Core.Application.Ports;

/// <summary>
/// Receives a password transiently and returns only the provider-neutral proof contract.
/// </summary>
public interface IProofProvider
{
    Task<ProofResult> ProveAsync(
        ProofRequest request,
        string password,
        CancellationToken cancellationToken = default);
}
