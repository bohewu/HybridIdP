using Core.Application.DTOs;

namespace Core.Application;

/// <summary>
/// Service interface for managing OIDC clients.
/// </summary>
public interface IClientService
{
    /// <summary>
    /// Get paginated, filtered, and sorted list of OIDC clients.
    /// </summary>
    Task<(IEnumerable<ClientSummary> items, int totalCount)> GetClientsAsync(
        int skip, 
        int take, 
        string? search, 
        string? type, 
        string? sort);

    /// <summary>
    /// Get detailed information about a specific client.
    /// </summary>
    Task<ClientDetail?> GetClientByIdAsync(Guid id);

    /// <summary>
    /// Create a new OIDC client.
    /// </summary>
    /// <returns>Created client details including generated secret if confidential.</returns>
    Task<CreateClientResponse> CreateClientAsync(CreateClientRequest request);

    /// <summary>
    /// Update an existing OIDC client.
    /// </summary>
    /// <exception cref="KeyNotFoundException">When client is not found.</exception>
    Task UpdateClientAsync(Guid id, UpdateClientRequest request);

    /// <summary>
    /// Delete an OIDC client.
    /// </summary>
    /// <exception cref="KeyNotFoundException">When client is not found.</exception>
    Task DeleteClientAsync(Guid id);

    /// <summary>
    /// Regenerate the secret for a confidential client.
    /// </summary>
    /// <returns>The new client secret.</returns>
    Task<string> RegenerateSecretAsync(Guid id);
}
