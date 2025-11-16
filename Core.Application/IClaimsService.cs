using Core.Application.DTOs;

namespace Core.Application;

/// <summary>
/// Service interface for managing user claim definitions.
/// Handles CRUD operations for both standard OIDC claims and custom enterprise claims.
/// </summary>
public interface IClaimsService
{
    /// <summary>
    /// Get paginated list of claim definitions with optional search, sorting, and filtering.
    /// </summary>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="search">Optional search term to filter by Name, DisplayName, Description, or ClaimType.</param>
    /// <param name="sortBy">Field to sort by (name, displayname, claimtype, type). Defaults to "name".</param>
    /// <param name="sortDirection">Sort direction (asc, desc). Defaults to "asc".</param>
    /// <returns>Tuple containing list of claims and total count before pagination.</returns>
    Task<(IEnumerable<ClaimDefinitionDto> items, int totalCount)> GetClaimsAsync(
        int skip,
        int take,
        string? search,
        string sortBy,
        string sortDirection);

    /// <summary>
    /// Get a specific claim definition by ID, including scope usage count.
    /// </summary>
    /// <param name="id">Claim ID.</param>
    /// <returns>Claim definition DTO, or null if not found.</returns>
    Task<ClaimDefinitionDto?> GetClaimByIdAsync(int id);

    /// <summary>
    /// Create a new custom claim definition.
    /// </summary>
    /// <param name="request">Claim creation request with required fields.</param>
    /// <returns>Created claim definition DTO.</returns>
    /// <exception cref="ArgumentException">Thrown when Name or ClaimType is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a claim with the same name already exists.</exception>
    Task<ClaimDefinitionDto> CreateClaimAsync(CreateClaimRequest request);

    /// <summary>
    /// Update an existing claim definition.
    /// For standard claims, only DisplayName and Description can be updated.
    /// </summary>
    /// <param name="id">Claim ID to update.</param>
    /// <param name="request">Update request with optional fields.</param>
    /// <returns>Updated claim definition DTO.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when claim with specified ID does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify core properties of standard claims.</exception>
    Task<ClaimDefinitionDto> UpdateClaimAsync(int id, UpdateClaimRequest request);

    /// <summary>
    /// Delete a claim definition.
    /// Cannot delete standard claims or claims that are currently used by scopes.
    /// </summary>
    /// <param name="id">Claim ID to delete.</param>
    /// <exception cref="KeyNotFoundException">Thrown when claim with specified ID does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to delete standard claims or claims in use.</exception>
    Task DeleteClaimAsync(int id);
}
