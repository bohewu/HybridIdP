using Core.Application.DTOs;

namespace Core.Application;

public interface IApiResourceService
{
    Task<(IEnumerable<ApiResourceSummary> items, int totalCount)> GetResourcesAsync(int skip, int take, string? search, string? sort);
    Task<ApiResourceDetail?> GetResourceByIdAsync(int id);
    Task<ApiResourceSummary> CreateResourceAsync(CreateApiResourceRequest request);
    Task<bool> UpdateResourceAsync(int id, UpdateApiResourceRequest request);
    Task<bool> DeleteResourceAsync(int id);
    Task<IEnumerable<ResourceScopeInfo>> GetResourceScopesAsync(int id);
}
