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
    private readonly Infrastructure.Authorization.IApiScopeUsagePolicy _scopeUsage;

    public ApiResourceService(
        IApplicationDbContext context,
        IOpenIddictScopeManager scopeManager,
        ILogger<ApiResourceService> logger,
        Infrastructure.Authorization.IApiScopeUsagePolicy scopeUsage)
    {
        _context = context;
        _scopeManager = scopeManager;
        _logger = logger;
        _scopeUsage = scopeUsage;
    }

    public async Task<(IEnumerable<ApiResourceSummary> items, int totalCount)> GetResourcesAsync(
        int skip, int take, string? search, string? sort, Guid? viewerPersonId = null)
    {
        var actor = await _scopeUsage.GetActorAsync();
        viewerPersonId = actor.IsAdmin ? null : actor.PersonId ?? Guid.Empty;
        var query = _context.ApiResources
            .Include(r => r.Scopes)
            .AsQueryable();
        if (!actor.IsAdmin)
            query = query.Where(r => r.IsCatalogVisible || (viewerPersonId != Guid.Empty && r.OwnerPersonId == viewerPersonId));

        // Search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r =>
                r.Name.Contains(search) ||
                (r.DisplayName != null && r.DisplayName.Contains(search)) ||
                (r.Description != null && r.Description.Contains(search)));
        }

        // Sorting
        query = (sort?.ToLowerInvariant()) switch
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
                IsUsageOpen = r.IsUsageOpen,
                IsCatalogVisible = r.IsCatalogVisible,
                ScopeCount = r.Scopes.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                IsOwner = viewerPersonId == null || (r.OwnerPersonId.HasValue && r.OwnerPersonId == viewerPersonId),
                IsReadOnly = viewerPersonId != null && (!r.OwnerPersonId.HasValue || r.OwnerPersonId != viewerPersonId)
            })
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<ApiResourceDetail?> GetResourceByIdAsync(int id, Guid? viewerPersonId = null)
    {
        var actor = await _scopeUsage.GetActorAsync();
        viewerPersonId = actor.IsAdmin ? null : actor.PersonId ?? Guid.Empty;
        var resource = await _context.ApiResources
            .Include(r => r.Scopes)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (resource == null || (!actor.IsAdmin && !resource.IsCatalogVisible &&
            (!actor.PersonId.HasValue || resource.OwnerPersonId != actor.PersonId)))
        {
            return null;
        }

        // Both supported mapping stores must be available to the resource's approval controls.
        var associatedScopeIds = resource.Scopes.Select(s => s.ScopeId).ToHashSet(StringComparer.Ordinal);
        await foreach (var scope in _scopeManager.FindByResourceAsync(resource.Name))
        {
            var scopeId = await _scopeManager.GetIdAsync(scope);
            if (scopeId != null) associatedScopeIds.Add(scopeId);
        }

        // Get scope details from OpenIddict
        var scopeInfos = new List<ResourceScopeInfo>();
        foreach (var scopeId in associatedScopeIds)
        {
            var scope = await _scopeManager.FindByIdAsync(scopeId);
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
            IsUsageOpen = resource.IsUsageOpen,
            IsCatalogVisible = resource.IsCatalogVisible,
            CreatedAt = resource.CreatedAt,
            UpdatedAt = resource.UpdatedAt,
            Scopes = scopeInfos,
            IsOwner = viewerPersonId == null || (resource.OwnerPersonId.HasValue && resource.OwnerPersonId == viewerPersonId),
            IsReadOnly = viewerPersonId != null && (!resource.OwnerPersonId.HasValue || resource.OwnerPersonId != viewerPersonId)
        };
    }

    public async Task<ApiResourceSummary> CreateResourceAsync(CreateApiResourceRequest request, Guid? ownerPersonId = null)
    {
        var actor = await _scopeUsage.GetActorAsync();
        ownerPersonId = actor.PersonId;
        await _scopeUsage.RequireResourceAuthorityAsync(new ApiResource { Name = request.Name, OwnerPersonId = ownerPersonId }, Core.Domain.Constants.Permissions.ApiResources.Create);
        await _scopeUsage.RequireScopeMappingAuthorityAsync(request.ScopeIds ?? []);
        await RequireNamedScopeMappingAuthorityAsync(request.Name);
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
            IsUsageOpen = request.IsUsageOpen,
            IsCatalogVisible = request.IsCatalogVisible,
            CreatedAt = DateTime.UtcNow,
            OwnerPersonId = ownerPersonId
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
            IsUsageOpen = resource.IsUsageOpen,
            IsCatalogVisible = resource.IsCatalogVisible,
            ScopeCount = request.ScopeIds?.Count ?? 0,
            CreatedAt = resource.CreatedAt,
            UpdatedAt = resource.UpdatedAt,
            IsOwner = true,
            IsReadOnly = false
        };
    }

    public async Task<bool> UpdateResourceAsync(int id, UpdateApiResourceRequest request, Guid? viewerPersonId = null)
    {
        var resource = await _context.ApiResources
            .Include(r => r.Scopes)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (resource == null)
        {
            return false;
        }

        await _scopeUsage.RequireResourceAuthorityAsync(resource, Core.Domain.Constants.Permissions.ApiResources.Update);
        if (request.ScopeIds != null)
            await _scopeUsage.RequireScopeMappingAuthorityAsync(request.ScopeIds.Except(resource.Scopes.Select(s => s.ScopeId), StringComparer.Ordinal));
        if (!string.Equals(resource.Name, request.Name, StringComparison.Ordinal))
            await RequireNamedScopeMappingAuthorityAsync(request.Name);

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
        if (request.IsUsageOpen.HasValue) resource.IsUsageOpen = request.IsUsageOpen.Value;
        if (request.IsCatalogVisible.HasValue) resource.IsCatalogVisible = request.IsCatalogVisible.Value;
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

    private async Task RequireNamedScopeMappingAuthorityAsync(string resourceName)
    {
        // Registering a name also adopts existing OpenIddict audience mappings.
        var scopeIds = new List<string>();
        await foreach (var scope in _scopeManager.FindByResourceAsync(resourceName))
        {
            var scopeId = await _scopeManager.GetIdAsync(scope);
            if (string.IsNullOrEmpty(scopeId))
                throw new InvalidOperationException("An associated scope has no identifier.");
            scopeIds.Add(scopeId);
        }
        await _scopeUsage.RequireScopeMappingAuthorityAsync(scopeIds);
    }

    public async Task<bool> DeleteResourceAsync(int id, Guid? viewerPersonId = null)
    {
        var resource = await _context.ApiResources
            .Include(r => r.Scopes)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (resource == null)
        {
            return false;
        }

        await _scopeUsage.RequireResourceAuthorityAsync(resource, Core.Domain.Constants.Permissions.ApiResources.Delete);

        // Scopes will be automatically removed due to cascade delete
        _context.ApiResources.Remove(resource);
        await _context.SaveChangesAsync(default);

        LogApiResourceDeleted(resource.Name, resource.Id);

        return true;
    }

    public async Task<IEnumerable<ResourceScopeInfo>> GetResourceScopesAsync(int id)
    {
        var resource = await GetResourceByIdAsync(id);
        return resource?.Scopes ?? [];
    }

    public Task ApproveClientScopeAsync(int resourceId, Guid applicationId, string scopeId, CancellationToken cancellationToken = default) =>
        _scopeUsage.ApproveAsync(applicationId, scopeId, resourceId, cancellationToken);

    public async Task<List<string>> GetAudiencesByScopesAsync(IEnumerable<string> scopeNames)
    {
        var scopeNamesList = scopeNames?.ToList();
        if (scopeNamesList == null || scopeNamesList.Count == 0)
        {
            return [];
        }
        
        // First, resolve scope names to scope IDs via OpenIddict
        var scopeIds = new List<string>();
        var namedResources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scopeName in scopeNamesList)
        {
            var scope = await _scopeManager.FindByNameAsync(scopeName);
            if (scope != null)
            {
                foreach (var resourceName in await _scopeManager.GetResourcesAsync(scope))
                    if (resourceName != Core.Domain.Constants.AuthConstants.Resources.ResourceServer)
                        namedResources.Add(resourceName);
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

        return audiences.Concat(namedResources).Distinct(StringComparer.Ordinal).ToList();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "API resource created: {ResourceName} (ID: {ResourceId})")]
    partial void LogApiResourceCreated(string resourceName, int resourceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "API resource updated: {ResourceName} (ID: {ResourceId})")]
    partial void LogApiResourceUpdated(string resourceName, int resourceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "API resource deleted: {ResourceName} (ID: {ResourceId})")]
    partial void LogApiResourceDeleted(string resourceName, int resourceId);
}
