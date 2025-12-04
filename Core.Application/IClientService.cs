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
    /// <param name="skip">Number of records to skip</param>
    /// <param name="take">Number of records to take</param>
    /// <param name="search">Search term for ClientId or DisplayName</param>
    /// <param name="type">Filter by client type</param>
    /// <param name="sort">Sort field and direction</param>
    /// <param name="ownerPersonId">Optional: Filter by owner PersonId (for ApplicationManager role)</param>
    Task<(IEnumerable<ClientSummary> items, int totalCount)> GetClientsAsync(
        int skip, 
        int take, 
        string? search, 
        string? type, 
        string? sort,
        Guid? ownerPersonId = null);

    /// <summary>
    /// Get detailed information about a specific client.
    /// </summary>
    Task<ClientDetail?> GetClientByIdAsync(Guid id);

    /// <summary>
    /// Create a new OIDC client.
    /// </summary>
    /// <param name="request">Client creation request</param>
    /// <param name="creatorUserId">The ApplicationUser ID creating this client</param>
    /// <param name="creatorPersonId">The Person ID owning this client (for ownership tracking)</param>
    /// <returns>Created client details including generated secret if confidential.</returns>
    Task<CreateClientResponse> CreateClientAsync(CreateClientRequest request, Guid? creatorUserId = null, Guid? creatorPersonId = null);

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

    /// <summary>
    /// Check if a user (by PersonId) owns a specific client.
    /// </summary>
    Task<bool> IsClientOwnedByPersonAsync(Guid clientId, Guid personId);
}
