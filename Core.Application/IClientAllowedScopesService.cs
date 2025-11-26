using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Application
{
    public interface IClientAllowedScopesService
    {
        Task<IReadOnlyList<string>> GetAllowedScopesAsync(Guid clientId);
        Task SetAllowedScopesAsync(Guid clientId, IEnumerable<string> scopes);
        Task<bool> IsScopeAllowedAsync(Guid clientId, string scope);
        Task<IReadOnlyList<string>> ValidateRequestedScopesAsync(Guid clientId, IEnumerable<string> requestedScopes);

        /// <summary>
        /// Gets required scopes for a specific client.
        /// Required scopes cannot be deselected during consent.
        /// </summary>
        Task<IReadOnlyList<string>> GetRequiredScopesAsync(Guid clientId);

        /// <summary>
        /// Sets required scopes for a specific client.
        /// Required scopes must be a subset of allowed scopes.
        /// </summary>
        /// <exception cref="InvalidOperationException">When a required scope is not in allowed scopes.</exception>
        Task SetRequiredScopesAsync(Guid clientId, IEnumerable<string> scopeNames);

        /// <summary>
        /// Checks if a specific scope is required for a client.
        /// </summary>
        Task<bool> IsScopeRequiredAsync(Guid clientId, string scopeName);
    }
}
