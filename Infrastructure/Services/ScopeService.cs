using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Services;

public class ScopeService : IScopeService
{
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IApplicationDbContext _db;

    public ScopeService(IOpenIddictScopeManager scopeManager, IOpenIddictApplicationManager applicationManager, IApplicationDbContext db)
    {
        _scopeManager = scopeManager;
        _applicationManager = applicationManager;
        _db = db;
    }

    public async Task<(IEnumerable<ScopeSummary> items, int totalCount)> GetScopesAsync(int skip, int take, string? search, string? sort)
    {
        var scopes = new List<ScopeSummary>();
        var scopeExtensions = await _db.ScopeExtensions.ToDictionaryAsync(se => se.ScopeId);
        
        await foreach (var scope in _scopeManager.ListAsync())
        {
            var id = await _scopeManager.GetIdAsync(scope);
            var name = await _scopeManager.GetNameAsync(scope);
            var displayName = await _scopeManager.GetDisplayNameAsync(scope);
            var description = await _scopeManager.GetDescriptionAsync(scope);
            var resources = await _scopeManager.GetResourcesAsync(scope);
            scopeExtensions.TryGetValue(id!, out var extension);
            
            scopes.Add(new ScopeSummary
            {
                Id = id!,
                Name = name!,
                DisplayName = displayName,
                Description = description,
                Resources = resources.ToList(),
                ConsentDisplayName = extension?.ConsentDisplayName,
                ConsentDescription = extension?.ConsentDescription,
                IconUrl = extension?.IconUrl,
                IsRequired = extension?.IsRequired ?? false,
                DisplayOrder = extension?.DisplayOrder ?? 0,
                Category = extension?.Category
            });
        }
        
        // Filtering
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            scopes = scopes.Where(x =>
                (!string.IsNullOrEmpty(x.Name) && x.Name.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.DisplayName) && x.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
        
        // Sorting
        string sortField = "name";
        bool sortAsc = true;
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var parts = sort.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0) sortField = parts[0].ToLowerInvariant();
            if (parts.Length > 1) sortAsc = !string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase);
        }
        
        Func<ScopeSummary, object?> keySelector = sortField switch
        {
            "displayname" => x => x.DisplayName,
            "description" => x => x.Description,
            _ => x => x.Name
        };
        
        scopes = (sortAsc ? scopes.OrderBy(keySelector) : scopes.OrderByDescending(keySelector)).ToList();
        var totalCount = scopes.Count;
        
        // Paging safety
        if (skip < 0) skip = 0;
        if (take <= 0) take = 25;
        var items = scopes.Skip(skip).Take(take).ToList();
        
        return (items, totalCount);
    }

    public async Task<ScopeSummary?> GetScopeByIdAsync(string id)
    {
        var scope = await _scopeManager.FindByIdAsync(id);
        if (scope == null) return null;
        
        var resources = await _scopeManager.GetResourcesAsync(scope);
        var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == id);
        
        return new ScopeSummary
        {
            Id = await _scopeManager.GetIdAsync(scope),
            Name = await _scopeManager.GetNameAsync(scope),
            DisplayName = await _scopeManager.GetDisplayNameAsync(scope),
            Description = await _scopeManager.GetDescriptionAsync(scope),
            Resources = resources.ToList(),
            ConsentDisplayName = extension?.ConsentDisplayName,
            ConsentDescription = extension?.ConsentDescription,
            IconUrl = extension?.IconUrl,
            IsRequired = extension?.IsRequired ?? false,
            DisplayOrder = extension?.DisplayOrder ?? 0,
            Category = extension?.Category
        };
    }

    public async Task<ScopeSummary> CreateScopeAsync(CreateScopeRequest request)
    {
        // Check if scope already exists
        var existing = await _scopeManager.FindByNameAsync(request.Name);
        if (existing != null)
        {
            throw new InvalidOperationException($"Scope '{request.Name}' already exists.");
        }
        
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Description = request.Description
        };
        
        if (request.Resources != null)
        {
            foreach (var resource in request.Resources)
            {
                descriptor.Resources.Add(resource);
            }
        }
        else
        {
            descriptor.Resources.Add(AuthConstants.Resources.ResourceServer);
        }
        
        var scope = await _scopeManager.CreateAsync(descriptor);
        var id = await _scopeManager.GetIdAsync(scope);
        
        // Create ScopeExtension for consent customization if any fields are provided
        if (!string.IsNullOrWhiteSpace(request.ConsentDisplayName) ||
            !string.IsNullOrWhiteSpace(request.ConsentDescription) ||
            !string.IsNullOrWhiteSpace(request.IconUrl) ||
            request.IsRequired ||
            request.DisplayOrder != 0 ||
            !string.IsNullOrWhiteSpace(request.Category))
        {
            var extension = new ScopeExtension
            {
                ScopeId = id!,
                ConsentDisplayName = request.ConsentDisplayName,
                ConsentDescription = request.ConsentDescription,
                IconUrl = request.IconUrl,
                IsRequired = request.IsRequired,
                DisplayOrder = request.DisplayOrder,
                Category = request.Category
            };
            _db.ScopeExtensions.Add(extension);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        
        return new ScopeSummary
        {
            Id = id!,
            Name = request.Name,
            DisplayName = descriptor.DisplayName,
            Description = descriptor.Description,
            Resources = descriptor.Resources.ToList(),
            ConsentDisplayName = request.ConsentDisplayName,
            ConsentDescription = request.ConsentDescription,
            IconUrl = request.IconUrl,
            IsRequired = request.IsRequired,
            DisplayOrder = request.DisplayOrder,
            Category = request.Category
        };
    }

    public async Task<bool> UpdateScopeAsync(string id, UpdateScopeRequest request)
    {
        var scope = await _scopeManager.FindByIdAsync(id);
        if (scope == null) return false;
        
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name ?? await _scopeManager.GetNameAsync(scope),
            DisplayName = request.DisplayName ?? await _scopeManager.GetDisplayNameAsync(scope),
            Description = request.Description ?? await _scopeManager.GetDescriptionAsync(scope)
        };
        
        var existingResources = await _scopeManager.GetResourcesAsync(scope);
        var resources = request.Resources ?? existingResources.ToList();
        foreach (var resource in resources)
        {
            descriptor.Resources.Add(resource);
        }
        
        await _scopeManager.PopulateAsync(scope, descriptor);
        await _scopeManager.UpdateAsync(scope);
        
        // Update or create ScopeExtension
        var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == id);
        
        if (extension == null)
        {
            if (!string.IsNullOrWhiteSpace(request.ConsentDisplayName) ||
                !string.IsNullOrWhiteSpace(request.ConsentDescription) ||
                !string.IsNullOrWhiteSpace(request.IconUrl) ||
                request.IsRequired == true ||
                request.DisplayOrder != null ||
                !string.IsNullOrWhiteSpace(request.Category))
            {
                extension = new ScopeExtension
                {
                    ScopeId = id!,
                    ConsentDisplayName = request.ConsentDisplayName,
                    ConsentDescription = request.ConsentDescription,
                    IconUrl = request.IconUrl,
                    IsRequired = request.IsRequired ?? false,
                    DisplayOrder = request.DisplayOrder ?? 0,
                    Category = request.Category
                };
                _db.ScopeExtensions.Add(extension);
            }
        }
        else
        {
            if (request.ConsentDisplayName != null)
                extension.ConsentDisplayName = request.ConsentDisplayName;
            if (request.ConsentDescription != null)
                extension.ConsentDescription = request.ConsentDescription;
            if (request.IconUrl != null)
                extension.IconUrl = request.IconUrl;
            if (request.IsRequired.HasValue)
                extension.IsRequired = request.IsRequired.Value;
            if (request.DisplayOrder.HasValue)
                extension.DisplayOrder = request.DisplayOrder.Value;
            if (request.Category != null)
                extension.Category = request.Category;
        }
        
        await _db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    public async Task<bool> DeleteScopeAsync(string id)
    {
        // Note: id is actually the scope name, not a GUID
        var scope = await _scopeManager.FindByNameAsync(id);
        if (scope == null) return false;
        
        // Check if scope is in use by any clients
        var clientsCount = 0;
        await foreach (var app in _applicationManager.ListAsync())
        {
            var permissions = await _applicationManager.GetPermissionsAsync(app);
            if (permissions.Any(p => p == $"{OpenIddictConstants.Permissions.Prefixes.Scope}{id}"))
            {
                clientsCount++;
                break;
            }
        }
        
        if (clientsCount > 0) return false;
        
        try
        {
            var scopeId = await _scopeManager.GetIdAsync(scope);
            var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == scopeId);
            if (extension != null)
            {
                _db.ScopeExtensions.Remove(extension);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            
            await _scopeManager.DeleteAsync(scope);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
