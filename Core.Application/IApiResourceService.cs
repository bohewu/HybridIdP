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
    
    /// <summary>
    /// Gets API resource names (audiences) associated with the provided scope names.
    /// Used for populating JWT 'aud' claim in access tokens.
    /// </summary>
    /// <param name="scopeNames">Collection of requested scope names</param>
    /// <returns>Distinct API resource names that should be included as audience values</returns>
    Task<List<string>> GetAudiencesByScopesAsync(IEnumerable<string> scopeNames);
}
