using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Web.IdP.Api;

/// <summary>
/// Scopes CRUD endpoints split from AdminController.
/// Routes preserved: api/admin/scopes/*
/// </summary>
[ApiController]
[Route("api/admin/scopes")]
[Authorize]
public class ScopesController : ControllerBase
{
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IApplicationDbContext _db;

    public ScopesController(
        IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager,
        IApplicationDbContext db)
    {
        _scopeManager = scopeManager;
        _applicationManager = applicationManager;
        _db = db;
    }

    /// <summary>
    /// Get all OIDC scopes with filtering, sorting, and pagination.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> GetScopes(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null)
    {
        var scopes = new List<ScopeSummary>();
        
        // Load all scope extensions for efficient lookup
        var scopeExtensions = await _db.ScopeExtensions.ToDictionaryAsync(se => se.ScopeId);
        
        await foreach (var scope in _scopeManager.ListAsync())
        {
            var id = await _scopeManager.GetIdAsync(scope);
            var name = await _scopeManager.GetNameAsync(scope);
            var displayName = await _scopeManager.GetDisplayNameAsync(scope);
            var description = await _scopeManager.GetDescriptionAsync(scope);
            var resources = await _scopeManager.GetResourcesAsync(scope);

            // Get scope extension if exists
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
            if (parts.Length > 0)
            {
                sortField = parts[0].ToLowerInvariant();
            }
            if (parts.Length > 1)
            {
                sortAsc = !string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase);
            }
        }

        Func<ScopeSummary, object?> keySelector = sortField switch
        {
            "displayname" => x => x.DisplayName,
            "description" => x => x.Description,
            _ => x => x.Name
        };

        scopes = (sortAsc
            ? scopes.OrderBy(keySelector)
            : scopes.OrderByDescending(keySelector)).ToList();

        var totalCount = scopes.Count;

        // Paging safety
        if (skip < 0) skip = 0;
        if (take <= 0) take = 25;

        var items = scopes.Skip(skip).Take(take).ToList();

        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Get a specific OIDC scope by ID.
    /// </summary>
    [HttpGet("{id}")]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> Get(string id)
    {
        var scope = await _scopeManager.FindByIdAsync(id);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{id}' not found." });
        }

        var resources = await _scopeManager.GetResourcesAsync(scope);
        var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == id);

        return Ok(new
        {
            id = await _scopeManager.GetIdAsync(scope),
            name = await _scopeManager.GetNameAsync(scope),
            displayName = await _scopeManager.GetDisplayNameAsync(scope),
            description = await _scopeManager.GetDescriptionAsync(scope),
            resources = resources.ToList(),
            consentDisplayName = extension?.ConsentDisplayName,
            consentDescription = extension?.ConsentDescription,
            iconUrl = extension?.IconUrl,
            isRequired = extension?.IsRequired ?? false,
            displayOrder = extension?.DisplayOrder ?? 0,
            category = extension?.Category
        });
    }

    /// <summary>
    /// Create a new OIDC scope.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Scopes.Create)]
    public async Task<ActionResult> Create([FromBody] CreateScopeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Scope name is required." });
        }

        // Check if scope already exists
        var existing = await _scopeManager.FindByNameAsync(request.Name);
        if (existing != null)
        {
            return Conflict(new { message = $"Scope '{request.Name}' already exists." });
        }

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Description = request.Description
        };

        // Add resources
        if (request.Resources != null)
        {
            foreach (var resource in request.Resources)
            {
                descriptor.Resources.Add(resource);
            }
        }
        else
        {
            // Default resource
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

        return CreatedAtAction(nameof(Get), new { id }, new
        {
            id,
            name = request.Name,
            displayName = descriptor.DisplayName,
            message = "Scope created successfully."
        });
    }

    /// <summary>
    /// Update an existing OIDC scope.
    /// </summary>
    [HttpPut("{id}")]
    [HasPermission(Permissions.Scopes.Update)]
    public async Task<ActionResult> Update(string id, [FromBody] UpdateScopeRequest request)
    {
        var scope = await _scopeManager.FindByIdAsync(id);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{id}' not found." });
        }

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name ?? await _scopeManager.GetNameAsync(scope),
            DisplayName = request.DisplayName ?? await _scopeManager.GetDisplayNameAsync(scope),
            Description = request.Description ?? await _scopeManager.GetDescriptionAsync(scope)
        };

        // Handle resources
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
            // Create new extension if any consent fields are provided
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
            // Update existing extension
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

        return Ok(new
        {
            id,
            message = "Scope updated successfully."
        });
    }

    /// <summary>
    /// Delete an OIDC scope.
    /// </summary>
    [HttpDelete("{id}")]
    [HasPermission(Permissions.Scopes.Delete)]
    public async Task<ActionResult> Delete(string id)
    {
        // Note: id is actually the scope name, not a GUID
        var scope = await _scopeManager.FindByNameAsync(id);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with name '{id}' not found." });
        }

        // Check if scope is in use by any clients
        var clientsCount = 0;
        await foreach (var app in _applicationManager.ListAsync())
        {
            var permissions = await _applicationManager.GetPermissionsAsync(app);
            if (permissions.Any(p => p == $"{OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope}{id}"))
            {
                clientsCount++;
                break; // Found at least one, that's enough
            }
        }

        if (clientsCount > 0)
        {
            return BadRequest(new { message = "Cannot delete this scope because it is currently in use by one or more clients. Please remove the scope from all clients first." });
        }

        try
        {
            // Get the actual scope ID for deletion
            var scopeId = await _scopeManager.GetIdAsync(scope);
            
            // Delete scope extension if exists
            var extension = await _db.ScopeExtensions.FirstOrDefaultAsync(se => se.ScopeId == scopeId);
            if (extension != null)
            {
                _db.ScopeExtensions.Remove(extension);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            
            await _scopeManager.DeleteAsync(scope);
            return Ok(new { message = "Scope deleted successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"An error occurred while deleting the scope: {ex.Message}" });
        }
    }
}
