using Core.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Application
{
    public interface IScopeService
    {
        Task<(IEnumerable<ScopeSummary> items, int totalCount)> GetScopesAsync(int skip, int take, string? search, string? sort);
        Task<ScopeSummary?> GetScopeByIdAsync(string id);
        Task<ScopeSummary> CreateScopeAsync(CreateScopeRequest request);
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
    }
}
