using Core.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Application
{
    public interface IScopeService
    {
        /// <summary>
        /// Get paginated, filtered, and sorted list of scopes.
        /// </summary>
        /// <param name="skip">Number of records to skip</param>
        /// <param name="take">Number of records to take</param>
        /// <param name="search">Search term</param>
        /// <param name="sort">Sort field and direction</param>
        /// <param name="ownerPersonId">Optional: Filter by owner PersonId (for ApplicationManager role)</param>
        Task<(IEnumerable<ScopeSummary> items, int totalCount)> GetScopesAsync(int skip, int take, string? search, string? sort, Guid? ownerPersonId = null);
        
        Task<ScopeSummary?> GetScopeByIdAsync(string id);
        
        /// <summary>
        /// Create a new scope.
        /// </summary>
        /// <param name="request">Scope creation request</param>
        /// <param name="creatorUserId">The ApplicationUser ID creating this scope</param>
        /// <param name="creatorPersonId">The Person ID owning this scope (for ownership tracking)</param>
        Task<ScopeSummary> CreateScopeAsync(CreateScopeRequest request, Guid? creatorUserId = null, Guid? creatorPersonId = null);
        
        Task<bool> UpdateScopeAsync(string id, UpdateScopeRequest request);
        Task<bool> DeleteScopeAsync(string id);

        /// <summary>
        /// Gets all claims associated with a specific scope.
        /// </summary>
        /// <param name="scopeId">The scope ID</param>
        /// <returns>Tuple containing scope ID, scope name, and list of associated claims</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the scope is not found</exception>
        Task<(string scopeId, string scopeName, IEnumerable<ScopeClaimDto> claims)> GetScopeClaimsAsync(string scopeId);

        /// <summary>
        /// Updates the claims associated with a specific scope.
        /// Removes all existing claims and adds the new ones.
        /// </summary>
        /// <param name="scopeId">The scope ID</param>
        /// <param name="request">Request containing list of claim IDs to associate</param>
        /// <returns>Tuple containing scope ID, scope name, and updated list of claims</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the scope is not found</exception>
        /// <exception cref="ArgumentException">Thrown when a claim ID is not found</exception>
        Task<(string scopeId, string scopeName, IEnumerable<ScopeClaimDto> claims)> UpdateScopeClaimsAsync(string scopeId, UpdateScopeClaimsRequest request);

        /// <summary>
        /// Classifies requested scopes into allowed (including required), required and rejected sets.
        /// Granted scopes are those explicitly consented by the user; required scopes are added regardless.
        /// </summary>
        /// <param name="requestedScopes">Scopes requested by the client.</param>
        /// <param name="availableScopes">Scopes available in the system (with IsRequired metadata).</param>
        /// <param name="grantedScopes">Scopes explicitly granted/consented by the user (may be null/empty).</param>
        /// <returns>Classification result including partial grant indicator.</returns>
        ScopeClassificationResult ClassifyScopes(IEnumerable<string> requestedScopes, IEnumerable<ScopeSummary> availableScopes, IEnumerable<string>? grantedScopes);

        /// <summary>
        /// Check if a user (by PersonId) owns a specific scope.
        /// </summary>
        Task<bool> IsScopeOwnedByPersonAsync(string scopeId, Guid personId);
    }
}
