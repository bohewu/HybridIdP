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

        /// <summary>
        /// Classifies requested scopes into allowed (including required), required and rejected sets.
        /// Granted scopes are those explicitly consented by the user; required scopes are added regardless.
        /// </summary>
        /// <param name="requestedScopes">Scopes requested by the client.</param>
        /// <param name="availableScopes">Scopes available in the system (with IsRequired metadata).</param>
        /// <param name="grantedScopes">Scopes explicitly granted/consented by the user (may be null/empty).</param>
        /// <returns>Classification result including partial grant indicator.</returns>
        ScopeClassificationResult ClassifyScopes(IEnumerable<string> requestedScopes, IEnumerable<ScopeSummary> availableScopes, IEnumerable<string>? grantedScopes);
    }
}
