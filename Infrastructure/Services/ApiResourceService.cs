using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

public partial class ApiResourceService : IApiResourceService
{
    private readonly IApplicationDbContext _context;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly ILogger<ApiResourceService> _logger;

    public ApiResourceService(
        IApplicationDbContext context,
        IOpenIddictScopeManager scopeManager,
        ILogger<ApiResourceService> logger)
    {
        _context = context;
        _scopeManager = scopeManager;
        _logger = logger;
    }

    public async Task<(IEnumerable<ApiResourceSummary> items, int totalCount)> GetResourcesAsync(
        int skip, int take, string? search, string? sort)
    {
        var query = _context.ApiResources
            .Include(r => r.Scopes)
            .AsQueryable();

        // Search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r =>
                r.Name.Contains(search) ||
                (r.DisplayName != null && r.DisplayName.Contains(search)) ||
                (r.Description != null && r.Description.Contains(search)));
        }

        // Sorting
        query = (sort?.ToLower()) switch
        {
            "name" => query.OrderBy(r => r.Name),
            "name_desc" => query.OrderByDescending(r => r.Name),
            "displayname" => query.OrderBy(r => r.DisplayName),
            "displayname_desc" => query.OrderByDescending(r => r.DisplayName),
            "createdat" => query.OrderBy(r => r.CreatedAt),
            "createdat_desc" => query.OrderByDescending(r => r.CreatedAt),
            _ => query.OrderBy(r => r.Name) // Default
        };

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(r => new ApiResourceSummary
            {
                Id = r.Id,
                Name = r.Name,
                DisplayName = r.DisplayName,
                Description = r.Description,
                BaseUrl = r.BaseUrl,
                ScopeCount = r.Scopes.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<ApiResourceDetail?> GetResourceByIdAsync(int id)
    {
        var resource = await _context.ApiResources
            .Include(r => r.Scopes)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (resource == null)
        {
            return null;
        }

        // Get scope details from OpenIddict
        var scopeInfos = new List<ResourceScopeInfo>();
        foreach (var resourceScope in resource.Scopes)
        {
            var scope = await _scopeManager.FindByIdAsync(resourceScope.ScopeId);
            if (scope != null)
            {
                scopeInfos.Add(new ResourceScopeInfo
                {
                    ScopeId = await _scopeManager.GetIdAsync(scope) ?? string.Empty,
                    Name = await _scopeManager.GetNameAsync(scope) ?? string.Empty,
                    DisplayName = await _scopeManager.GetDisplayNameAsync(scope),
                    Description = await _scopeManager.GetDescriptionAsync(scope)
                });
            }
        }

        return new ApiResourceDetail
        {
            Id = resource.Id,
            Name = resource.Name,
            DisplayName = resource.DisplayName,
            Description = resource.Description,
            BaseUrl = resource.BaseUrl,
            CreatedAt = resource.CreatedAt,
            UpdatedAt = resource.UpdatedAt,
            Scopes = scopeInfos
        };
    }

    public async Task<ApiResourceSummary> CreateResourceAsync(CreateApiResourceRequest request)
    {
        // Check for duplicate name
        var exists = await _context.ApiResources
            .AnyAsync(r => r.Name == request.Name);

        if (exists)
        {
            throw new InvalidOperationException($"API resource with name '{request.Name}' already exists.");
        }

        var resource = new ApiResource
        {
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            BaseUrl = request.BaseUrl,
            CreatedAt = DateTime.UtcNow
        };

        _context.ApiResources.Add(resource);
        await _context.SaveChangesAsync(default);

        // Add scope associations if provided
        if (request.ScopeIds != null && request.ScopeIds.Count > 0)
        {
            foreach (var scopeId in request.ScopeIds.Distinct())
            {
                // Verify scope exists
                var scope = await _scopeManager.FindByIdAsync(scopeId);
                if (scope != null)
                {
                    _context.ApiResourceScopes.Add(new ApiResourceScope
                    {
                        ApiResourceId = resource.Id,
                        ScopeId = scopeId
                    });
                }
            }
            await _context.SaveChangesAsync(default);
        }

        LogApiResourceCreated(resource.Name, resource.Id);

        return new ApiResourceSummary
        {
            Id = resource.Id,
            Name = resource.Name,
            DisplayName = resource.DisplayName,
            Description = resource.Description,
            BaseUrl = resource.BaseUrl,
            ScopeCount = request.ScopeIds?.Count ?? 0,
            CreatedAt = resource.CreatedAt,
            UpdatedAt = resource.UpdatedAt
        };
    }

    public async Task<bool> UpdateResourceAsync(int id, UpdateApiResourceRequest request)
    {
        var resource = await _context.ApiResources
            .Include(r => r.Scopes)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (resource == null)
        {
            return false;
        }

        // Check for duplicate name (excluding current resource)
        var duplicateExists = await _context.ApiResources
            .AnyAsync(r => r.Name == request.Name && r.Id != id);

        if (duplicateExists)
        {
            throw new InvalidOperationException($"API resource with name '{request.Name}' already exists.");
        }

        // Update basic properties
        resource.Name = request.Name;
        resource.DisplayName = request.DisplayName;
        resource.Description = request.Description;
        resource.BaseUrl = request.BaseUrl;
        resource.UpdatedAt = DateTime.UtcNow;

        // Update scope associations
        if (request.ScopeIds != null)
        {
            // Remove existing scopes
            _context.ApiResourceScopes.RemoveRange(resource.Scopes);

            // Add new scopes
            foreach (var scopeId in request.ScopeIds.Distinct())
            {
                // Verify scope exists
                var scope = await _scopeManager.FindByIdAsync(scopeId);
                if (scope != null)
                {
                    _context.ApiResourceScopes.Add(new ApiResourceScope
                    {
                        ApiResourceId = resource.Id,
                        ScopeId = scopeId
                    });
                }
            }
        }

        await _context.SaveChangesAsync(default);

        LogApiResourceUpdated(resource.Name, resource.Id);

        return true;
    }

    public async Task<bool> DeleteResourceAsync(int id)
    {
        var resource = await _context.ApiResources
            .Include(r => r.Scopes)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (resource == null)
        {
            return false;
        }

        // Scopes will be automatically removed due to cascade delete
        _context.ApiResources.Remove(resource);
        await _context.SaveChangesAsync(default);

        LogApiResourceDeleted(resource.Name, resource.Id);

        return true;
    }

    public async Task<IEnumerable<ResourceScopeInfo>> GetResourceScopesAsync(int id)
    {
        var resource = await _context.ApiResources
            .Include(r => r.Scopes)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (resource == null)
        {
            return Enumerable.Empty<ResourceScopeInfo>();
        }

        var scopeInfos = new List<ResourceScopeInfo>();
        foreach (var resourceScope in resource.Scopes)
        {
            var scope = await _scopeManager.FindByIdAsync(resourceScope.ScopeId);
            if (scope != null)
            {
                scopeInfos.Add(new ResourceScopeInfo
                {
                    ScopeId = await _scopeManager.GetIdAsync(scope) ?? string.Empty,
                    Name = await _scopeManager.GetNameAsync(scope) ?? string.Empty,
                    DisplayName = await _scopeManager.GetDisplayNameAsync(scope),
                    Description = await _scopeManager.GetDescriptionAsync(scope)
                });
            }
        }

        return scopeInfos;
    }

    public async Task<List<string>> GetAudiencesByScopesAsync(IEnumerable<string> scopeNames)
    {
        if (scopeNames == null || !scopeNames.Any())
        {
            return [];
        }

        var scopeNamesList = scopeNames.ToList();
        
        // First, resolve scope names to scope IDs via OpenIddict
        var scopeIds = new List<string>();
        foreach (var scopeName in scopeNamesList)
        {
            var scope = await _scopeManager.FindByNameAsync(scopeName);
            if (scope != null)
            {
                var scopeId = await _scopeManager.GetIdAsync(scope);
                if (!string.IsNullOrEmpty(scopeId))
                {
                    scopeIds.Add(scopeId);
                }
            }
        }

        if (scopeIds.Count == 0)
        {
            return [];
        }

        // Query API resources that contain these scope IDs
        var audiences = await _context.ApiResourceScopes
            .Where(ars => scopeIds.Contains(ars.ScopeId))
            .Include(ars => ars.ApiResource)
            .Select(ars => ars.ApiResource!.Name)
            .Distinct()
            .ToListAsync();

        return audiences;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "API resource created: {ResourceName} (ID: {ResourceId})")]
    partial void LogApiResourceCreated(string resourceName, int resourceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "API resource updated: {ResourceName} (ID: {ResourceId})")]
    partial void LogApiResourceUpdated(string resourceName, int resourceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "API resource deleted: {ResourceName} (ID: {ResourceId})")]
    partial void LogApiResourceDeleted(string resourceName, int resourceId);
}
