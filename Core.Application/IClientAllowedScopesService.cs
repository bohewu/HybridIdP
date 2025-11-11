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
    }
}
