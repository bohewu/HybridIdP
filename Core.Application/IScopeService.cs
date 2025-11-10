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
    }
}
