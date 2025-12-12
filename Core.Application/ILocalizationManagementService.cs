using System.Threading.Tasks;
using Core.Application.DTOs;

namespace Core.Application;

public interface ILocalizationManagementService
{
    Task<(IEnumerable<ResourceDto> Items, int TotalCount)> GetResourcesAsync(int skip, int take, string? search, string? sort);
    Task<ResourceDto?> GetResourceByIdAsync(int id);
    Task<ResourceDto> CreateResourceAsync(CreateResourceRequest request);
    Task<bool> UpdateResourceAsync(int id, UpdateResourceRequest request);
    Task<bool> DeleteResourceAsync(int id);
}
