using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using System.Linq;
using Infrastructure;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using DomainPermissions = Core.Domain.Constants.Permissions;

namespace Web.IdP.Api;

/// <summary>
/// Admin API controller for management operations.
/// All endpoints require specific permissions (enforced via HasPermission attribute).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // Require authentication, permissions checked per-endpoint
public class AdminController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly IUserManagementService _userManagementService;
    private readonly IRoleManagementService _roleManagementService;

    public AdminController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        IUserManagementService userManagementService,
        IRoleManagementService roleManagementService)
    {
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _userManager = userManager;
        _context = context;
        _userManagementService = userManagementService;
        _roleManagementService = roleManagementService;
    }

    /// <summary>
    /// Health check endpoint to verify admin API is accessible and authorization is working.
    /// </summary>
    /// <returns>OK with a simple status message.</returns>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            message = "Admin API is accessible",
            timestamp = DateTime.UtcNow,
            user = User.Identity?.Name
        });
    }

    /// <summary>
    /// Get dashboard statistics including total counts of clients, scopes, and users.
    /// </summary>
    /// <returns>Dashboard stats DTO with total counts.</returns>
    [HttpGet("dashboard/stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        // Count total clients
        var totalClients = 0;
        await foreach (var _ in _applicationManager.ListAsync())
        {
            totalClients++;
        }

        // Count total scopes
        var totalScopes = 0;
        await foreach (var _ in _scopeManager.ListAsync())
        {
            totalScopes++;
        }

        // Count total users
        var totalUsers = _userManager.Users.Count();

        return Ok(new DashboardStatsDto
        {
            TotalClients = totalClients,
            TotalScopes = totalScopes,
            TotalUsers = totalUsers
        });
    }

    #region OIDC Clients

    /// <summary>
    /// Get OIDC clients with server-side paging, filtering and sorting.
    /// </summary>
    /// <param name="skip">Number of items to skip (default: 0)</param>
    /// <param name="take">Number of items to take (default: 25)</param>
    /// <param name="search">Optional search string matched against clientId/displayName (case-insensitive)</param>
    /// <param name="type">Optional client type filter: "public" | "confidential"</param>
    /// <param name="sort">Optional sort expression, e.g. "clientId:asc" (fields: clientId, displayName, type, redirectUrisCount)</param>
    [HttpGet("clients")]
    [HasPermission(DomainPermissions.Clients.Read)]
    public async Task<IActionResult> GetClients(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] string? sort = null)
    {
        var summaries = new List<ClientSummary>();

        await foreach (var application in _applicationManager.ListAsync())
        {
            var id = await _applicationManager.GetIdAsync(application);
            var clientId = await _applicationManager.GetClientIdAsync(application);
            var displayName = await _applicationManager.GetDisplayNameAsync(application);
            var clientType = await _applicationManager.GetClientTypeAsync(application);
            var consentType = await _applicationManager.GetConsentTypeAsync(application);
            var applicationType = await _applicationManager.GetApplicationTypeAsync(application);
            var redirectUris = await _applicationManager.GetRedirectUrisAsync(application);

            summaries.Add(new ClientSummary
            {
                Id = id!,
                ClientId = clientId!,
                DisplayName = displayName,
                Type = clientType!,
                ApplicationType = applicationType!,
                ConsentType = consentType!,
                RedirectUrisCount = redirectUris.Count()
            });
        }

        // Filtering
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            summaries = summaries.Where(x =>
                (!string.IsNullOrEmpty(x.ClientId) && x.ClientId.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.DisplayName) && x.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim().ToLowerInvariant();
            summaries = summaries.Where(x => string.Equals(x.Type, t, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Sorting
        string sortField = "clientId";
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

        Func<ClientSummary, object?> keySelector = sortField switch
        {
            "displayname" => x => x.DisplayName,
            "type" => x => x.Type,
            "redirecturiscnt" => x => x.RedirectUrisCount,
            "redirecturicount" => x => x.RedirectUrisCount,
            _ => x => x.ClientId
        };

        summaries = (sortAsc
            ? summaries.OrderBy(keySelector)
            : summaries.OrderByDescending(keySelector)).ToList();

        var totalCount = summaries.Count;

        // Paging safety
        if (skip < 0) skip = 0;
        if (take <= 0) take = 25;

        var items = summaries.Skip(skip).Take(take).ToList();

        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Get a specific OIDC client by ID.
    /// </summary>
    [HttpGet("clients/{id}")]
    [HasPermission(DomainPermissions.Clients.Read)]
    public async Task<IActionResult> GetClient(string id)
    {
        var application = await _applicationManager.FindByIdAsync(id);
        if (application == null)
        {
            return NotFound(new { message = $"Client with ID '{id}' not found." });
        }

        var redirectUris = await _applicationManager.GetRedirectUrisAsync(application);
        var postLogoutUris = await _applicationManager.GetPostLogoutRedirectUrisAsync(application);
        var permissions = await _applicationManager.GetPermissionsAsync(application);

        return Ok(new
        {
            id = await _applicationManager.GetIdAsync(application),
            clientId = await _applicationManager.GetClientIdAsync(application),
            displayName = await _applicationManager.GetDisplayNameAsync(application),
            type = await _applicationManager.GetClientTypeAsync(application),
            consentType = await _applicationManager.GetConsentTypeAsync(application),
            redirectUris = redirectUris.ToList(),
            postLogoutRedirectUris = postLogoutUris.ToList(),
            permissions = permissions.ToList()
        });
    }

    /// <summary>
    /// Create a new OIDC client.
    /// </summary>
    [HttpPost("clients")]
    [HasPermission(DomainPermissions.Clients.Create)]
    public async Task<IActionResult> CreateClient([FromBody] CreateClientRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { message = "ClientId is required." });
        }

        // Check if client already exists
        var existing = await _applicationManager.FindByClientIdAsync(request.ClientId);
        if (existing != null)
        {
            return Conflict(new { message = $"Client with ID '{request.ClientId}' already exists." });
        }

        // Determine client type - use provided or infer from secret
        var clientType = request.Type;
        if (string.IsNullOrEmpty(clientType))
        {
            // If not specified, infer from secret presence
            clientType = string.IsNullOrEmpty(request.ClientSecret) ? ClientTypes.Public : ClientTypes.Confidential;
        }

        // Validate client type and secret combination
        if (clientType == ClientTypes.Confidential)
        {
            if (string.IsNullOrEmpty(request.ClientSecret))
            {
                return BadRequest(new { message = "Confidential clients must have a ClientSecret." });
            }
        }
        else if (clientType == ClientTypes.Public)
        {
            if (!string.IsNullOrEmpty(request.ClientSecret))
            {
                return BadRequest(new { message = "Public clients should not have a ClientSecret. Remove the secret or select Confidential client type." });
            }
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = request.ClientId,
            ClientSecret = request.ClientSecret,
            DisplayName = request.DisplayName ?? request.ClientId,
            ConsentType = request.ConsentType ?? ConsentTypes.Explicit,
            ApplicationType = request.ApplicationType ?? ApplicationTypes.Web,  // Default to web if not specified
            ClientType = clientType
        };

        // Add redirect URIs
        if (request.RedirectUris != null)
        {
            foreach (var uri in request.RedirectUris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var validUri))
                {
                    descriptor.RedirectUris.Add(validUri);
                }
            }
        }

        // Add post logout redirect URIs
        if (request.PostLogoutRedirectUris != null)
        {
            foreach (var uri in request.PostLogoutRedirectUris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var validUri))
                {
                    descriptor.PostLogoutRedirectUris.Add(validUri);
                }
            }
        }

        // Add permissions
        if (request.Permissions != null)
        {
            foreach (var permission in request.Permissions)
            {
                descriptor.Permissions.Add(permission);
            }
        }
        else
        {
            // Default permissions for authorization code flow
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Email);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Profile);
            descriptor.Permissions.Add($"{OpenIddictConstants.Permissions.Prefixes.Scope}{AuthConstants.Scopes.OpenId}");
        }

        var application = await _applicationManager.CreateAsync(descriptor);
        var id = await _applicationManager.GetIdAsync(application);

        return CreatedAtAction(nameof(GetClient), new { id }, new
        {
            id,
            clientId = request.ClientId,
            displayName = descriptor.DisplayName,
            message = "Client created successfully."
        });
    }

    /// <summary>
    /// Update an existing OIDC client.
    /// </summary>
    [HttpPut("clients/{id}")]
    [HasPermission(DomainPermissions.Clients.Update)]
    public async Task<IActionResult> UpdateClient(string id, [FromBody] UpdateClientRequest request)
    {
        var application = await _applicationManager.FindByIdAsync(id);
        if (application == null)
        {
            return NotFound(new { message = $"Client with ID '{id}' not found." });
        }

        // Get descriptor populated from existing application to preserve all properties
        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application);

        // Ensure ApplicationType and ClientType are set (fix for existing apps without type)
        if (string.IsNullOrEmpty(descriptor.ApplicationType))
        {
            descriptor.ApplicationType = ApplicationTypes.Web;
        }
        
        if (string.IsNullOrEmpty(descriptor.ClientType))
        {
            // Determine based on whether there's currently a secret
            var hasSecret = !string.IsNullOrEmpty(descriptor.ClientSecret);
            descriptor.ClientType = hasSecret ? ClientTypes.Confidential : ClientTypes.Public;
        }

        // Update only the fields provided in the request
        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            descriptor.ClientId = request.ClientId;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            descriptor.DisplayName = request.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(request.ConsentType))
        {
            descriptor.ConsentType = request.ConsentType;
        }

        // Only set ClientSecret if a new one is explicitly provided
        if (!string.IsNullOrEmpty(request.ClientSecret))
        {
            descriptor.ClientSecret = request.ClientSecret;
            descriptor.ClientType = ClientTypes.Confidential;  // Update type if adding/changing secret
        }

        // Handle redirect URIs - replace if provided
        if (request.RedirectUris != null)
        {
            descriptor.RedirectUris.Clear();
            foreach (var uri in request.RedirectUris)
            {
                descriptor.RedirectUris.Add(new Uri(uri));
            }
        }

        // Handle post logout redirect URIs - replace if provided
        if (request.PostLogoutRedirectUris != null)
        {
            descriptor.PostLogoutRedirectUris.Clear();
            foreach (var uri in request.PostLogoutRedirectUris)
            {
                descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
            }
        }

        // Handle permissions - replace if provided
        if (request.Permissions != null)
        {
            descriptor.Permissions.Clear();
            foreach (var permission in request.Permissions)
            {
                descriptor.Permissions.Add(permission);
            }
        }

        await _applicationManager.PopulateAsync(application, descriptor);
        await _applicationManager.UpdateAsync(application);

        return Ok(new
        {
            id,
            message = "Client updated successfully."
        });
    }

    /// <summary>
    /// Delete an OIDC client.
    /// </summary>
    [HttpDelete("clients/{id}")]
    [HasPermission(DomainPermissions.Clients.Delete)]
    public async Task<IActionResult> DeleteClient(string id)
    {
        var application = await _applicationManager.FindByIdAsync(id);
        if (application == null)
        {
            return NotFound(new { message = $"Client with ID '{id}' not found." });
        }

        await _applicationManager.DeleteAsync(application);

        return Ok(new { message = "Client deleted successfully." });
    }

    #endregion

    #region OIDC Scopes

    /// <summary>
    /// Get all OIDC scopes.
    /// </summary>
    [HttpGet("scopes")]
    [HasPermission(DomainPermissions.Scopes.Read)]
    public async Task<IActionResult> GetScopes()
    {
        var scopes = new List<object>();
        
        await foreach (var scope in _scopeManager.ListAsync())
        {
            scopes.Add(new
            {
                id = await _scopeManager.GetIdAsync(scope),
                name = await _scopeManager.GetNameAsync(scope),
                displayName = await _scopeManager.GetDisplayNameAsync(scope),
                description = await _scopeManager.GetDescriptionAsync(scope)
            });
        }

        return Ok(scopes);
    }

    /// <summary>
    /// Get a specific OIDC scope by ID.
    /// </summary>
    [HttpGet("scopes/{id}")]
    [HasPermission(DomainPermissions.Scopes.Read)]
    public async Task<IActionResult> GetScope(string id)
    {
        var scope = await _scopeManager.FindByIdAsync(id);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{id}' not found." });
        }

        var resources = await _scopeManager.GetResourcesAsync(scope);

        return Ok(new
        {
            id = await _scopeManager.GetIdAsync(scope),
            name = await _scopeManager.GetNameAsync(scope),
            displayName = await _scopeManager.GetDisplayNameAsync(scope),
            description = await _scopeManager.GetDescriptionAsync(scope),
            resources = resources.ToList()
        });
    }

    /// <summary>
    /// Create a new OIDC scope.
    /// </summary>
    [HttpPost("scopes")]
    [HasPermission(DomainPermissions.Scopes.Create)]
    public async Task<IActionResult> CreateScope([FromBody] CreateScopeRequest request)
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

        return CreatedAtAction(nameof(GetScope), new { id }, new
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
    [HttpPut("scopes/{id}")]
    [HasPermission(DomainPermissions.Scopes.Update)]
    public async Task<IActionResult> UpdateScope(string id, [FromBody] UpdateScopeRequest request)
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

        return Ok(new
        {
            id,
            message = "Scope updated successfully."
        });
    }

    /// <summary>
    /// Delete an OIDC scope.
    /// </summary>
    [HttpDelete("scopes/{id}")]
    [HasPermission(DomainPermissions.Scopes.Delete)]
    public async Task<IActionResult> DeleteScope(string id)
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
            await _scopeManager.DeleteAsync(scope);
            return Ok(new { message = "Scope deleted successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"An error occurred while deleting the scope: {ex.Message}" });
        }
    }

    #endregion

    #region User Claims Management

    /// <summary>
    /// Get all user claim definitions.
    /// </summary>
    [HasPermission(DomainPermissions.Scopes.Read)]
    [HttpGet("claims")]
    public async Task<IActionResult> GetClaims()
    {
        var claims = await _context.Set<Core.Domain.Entities.UserClaim>()
            .Include(c => c.ScopeClaims)
            .Select(c => new ClaimDefinitionDto
            {
                Id = c.Id,
                Name = c.Name,
                DisplayName = c.DisplayName,
                Description = c.Description,
                ClaimType = c.ClaimType,
                UserPropertyPath = c.UserPropertyPath,
                DataType = c.DataType,
                IsStandard = c.IsStandard,
                IsRequired = c.IsRequired,
                ScopeCount = c.ScopeClaims.Count
            })
            .OrderBy(c => c.Name)
            .ToListAsync();

        return Ok(claims);
    }

    /// <summary>
    /// Get a specific user claim definition by ID.
    /// </summary>
    [HasPermission(DomainPermissions.Scopes.Read)]
    [HttpGet("claims/{id:int}")]
    public async Task<IActionResult> GetClaim(int id)
    {
        var claim = await _context.Set<Core.Domain.Entities.UserClaim>()
            .Include(c => c.ScopeClaims)
            .Where(c => c.Id == id)
            .Select(c => new ClaimDefinitionDto
            {
                Id = c.Id,
                Name = c.Name,
                DisplayName = c.DisplayName,
                Description = c.Description,
                ClaimType = c.ClaimType,
                UserPropertyPath = c.UserPropertyPath,
                DataType = c.DataType,
                IsStandard = c.IsStandard,
                IsRequired = c.IsRequired,
                ScopeCount = c.ScopeClaims.Count
            })
            .FirstOrDefaultAsync();

        if (claim == null)
        {
            return NotFound(new { message = $"Claim with ID {id} not found." });
        }

        return Ok(claim);
    }

    /// <summary>
    /// Create a new user claim definition.
    /// </summary>
    [HasPermission(DomainPermissions.Scopes.Create)]
    [HttpPost("claims")]
    public async Task<IActionResult> CreateClaim([FromBody] CreateClaimRequest request)
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ClaimType))
        {
            return BadRequest(new { message = "Name and ClaimType are required." });
        }

        // Check if claim name already exists
        var existingClaim = await _context.Set<Core.Domain.Entities.UserClaim>()
            .FirstOrDefaultAsync(c => c.Name == request.Name);

        if (existingClaim != null)
        {
            return BadRequest(new { message = $"A claim with name '{request.Name}' already exists." });
        }

        // Create new claim
        var claim = new Core.Domain.Entities.UserClaim
        {
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Description = request.Description,
            ClaimType = request.ClaimType,
            UserPropertyPath = request.UserPropertyPath ?? request.Name,
            DataType = request.DataType ?? "String",
            IsStandard = false, // Custom claims are always non-standard
            IsRequired = request.IsRequired ?? false
        };

        _context.Set<Core.Domain.Entities.UserClaim>().Add(claim);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetClaim), new { id = claim.Id }, new ClaimDefinitionDto
        {
            Id = claim.Id,
            Name = claim.Name,
            DisplayName = claim.DisplayName,
            Description = claim.Description,
            ClaimType = claim.ClaimType,
            UserPropertyPath = claim.UserPropertyPath,
            DataType = claim.DataType,
            IsStandard = claim.IsStandard,
            IsRequired = claim.IsRequired,
            ScopeCount = 0
        });
    }

    /// <summary>
    /// Update an existing user claim definition.
    /// </summary>
    [HasPermission(DomainPermissions.Scopes.Update)]
    [HttpPut("claims/{id:int}")]
    public async Task<IActionResult> UpdateClaim(int id, [FromBody] UpdateClaimRequest request)
    {
        var claim = await _context.Set<Core.Domain.Entities.UserClaim>()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (claim == null)
        {
            return NotFound(new { message = $"Claim with ID {id} not found." });
        }

        // Prevent modification of standard claims' core properties
        if (claim.IsStandard)
        {
            return BadRequest(new { message = "Cannot modify standard OIDC claims. Only DisplayName and Description can be updated." });
        }

        // Update properties
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            claim.DisplayName = request.DisplayName;
        
        if (request.Description != null)
            claim.Description = request.Description;

        if (!claim.IsStandard)
        {
            if (!string.IsNullOrWhiteSpace(request.ClaimType))
                claim.ClaimType = request.ClaimType;
            
            if (!string.IsNullOrWhiteSpace(request.UserPropertyPath))
                claim.UserPropertyPath = request.UserPropertyPath;
            
            if (!string.IsNullOrWhiteSpace(request.DataType))
                claim.DataType = request.DataType;
            
            if (request.IsRequired.HasValue)
                claim.IsRequired = request.IsRequired.Value;
        }

        await _context.SaveChangesAsync();

        return Ok(new ClaimDefinitionDto
        {
            Id = claim.Id,
            Name = claim.Name,
            DisplayName = claim.DisplayName,
            Description = claim.Description,
            ClaimType = claim.ClaimType,
            UserPropertyPath = claim.UserPropertyPath,
            DataType = claim.DataType,
            IsStandard = claim.IsStandard,
            IsRequired = claim.IsRequired,
            ScopeCount = claim.ScopeClaims.Count
        });
    }

    /// <summary>
    /// Delete a user claim definition.
    /// </summary>
    [HasPermission(DomainPermissions.Scopes.Delete)]
    [HttpDelete("claims/{id:int}")]
    public async Task<IActionResult> DeleteClaim(int id)
    {
        var claim = await _context.Set<Core.Domain.Entities.UserClaim>()
            .Include(c => c.ScopeClaims)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (claim == null)
        {
            return NotFound(new { message = $"Claim with ID {id} not found." });
        }

        // Prevent deletion of standard claims
        if (claim.IsStandard)
        {
            return BadRequest(new { message = "Cannot delete standard OIDC claims." });
        }

        // Check if claim is used by any scopes
        if (claim.ScopeClaims.Any())
        {
            return BadRequest(new { message = $"Cannot delete claim '{claim.Name}' because it is used by {claim.ScopeClaims.Count} scope(s)." });
        }

        _context.Set<Core.Domain.Entities.UserClaim>().Remove(claim);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Claim deleted successfully." });
    }

    #endregion

    #region Scope-to-Claims Mapping

    /// <summary>
    /// Get all claims associated with a specific scope.
    /// </summary>
    [HasPermission(DomainPermissions.Scopes.Read)]
    [HttpGet("scopes/{scopeId}/claims")]
    public async Task<IActionResult> GetScopeClaims(string scopeId)
    {
        // Verify scope exists
        var scope = await _scopeManager.FindByIdAsync(scopeId);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{scopeId}' not found." });
        }

        var scopeName = await _scopeManager.GetNameAsync(scope);

        // Get all claims associated with this scope
        var scopeClaims = await _context.Set<ScopeClaim>()
            .Include(sc => sc.UserClaim)
            .Where(sc => sc.ScopeId == scopeId)
            .Select(sc => new ScopeClaimDto
            {
                Id = sc.Id,
                ScopeId = sc.ScopeId,
                ScopeName = sc.ScopeName,
                ClaimId = sc.UserClaimId,
                ClaimName = sc.UserClaim!.Name,
                ClaimDisplayName = sc.UserClaim.DisplayName,
                ClaimType = sc.UserClaim.ClaimType,
                AlwaysInclude = sc.AlwaysInclude,
                CustomMappingLogic = sc.CustomMappingLogic
            })
            .ToListAsync();

        return Ok(new
        {
            scopeId,
            scopeName,
            claims = scopeClaims
        });
    }

    /// <summary>
    /// Update the claims associated with a specific scope.
    /// </summary>
    [HasPermission(DomainPermissions.Scopes.Update)]
    [HttpPut("scopes/{scopeId}/claims")]
    public async Task<IActionResult> UpdateScopeClaims(string scopeId, [FromBody] UpdateScopeClaimsRequest request)
    {
        // Verify scope exists
        var scope = await _scopeManager.FindByIdAsync(scopeId);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{scopeId}' not found." });
        }

        var scopeName = await _scopeManager.GetNameAsync(scope);

        // Remove existing scope claims
        var existingScopeClaims = await _context.Set<ScopeClaim>()
            .Where(sc => sc.ScopeId == scopeId)
            .ToListAsync();

        _context.Set<ScopeClaim>().RemoveRange(existingScopeClaims);

        // Add new scope claims
        if (request.ClaimIds != null && request.ClaimIds.Any())
        {
            foreach (var claimId in request.ClaimIds)
            {
                // Verify claim exists
                var claim = await _context.Set<UserClaim>()
                    .FirstOrDefaultAsync(c => c.Id == claimId);

                if (claim == null)
                {
                    return BadRequest(new { message = $"Claim with ID {claimId} not found." });
                }

                var scopeClaim = new ScopeClaim
                {
                    ScopeId = scopeId,
                    ScopeName = scopeName ?? "",
                    UserClaimId = claimId,
                    AlwaysInclude = claim.IsRequired // Always include required claims
                };

                _context.Set<ScopeClaim>().Add(scopeClaim);
            }
        }

        await _context.SaveChangesAsync();

        // Return updated claims
        var updatedClaims = await _context.Set<ScopeClaim>()
            .Include(sc => sc.UserClaim)
            .Where(sc => sc.ScopeId == scopeId)
            .Select(sc => new ScopeClaimDto
            {
                Id = sc.Id,
                ScopeId = sc.ScopeId,
                ScopeName = sc.ScopeName,
                ClaimId = sc.UserClaimId,
                ClaimName = sc.UserClaim!.Name,
                ClaimDisplayName = sc.UserClaim.DisplayName,
                ClaimType = sc.UserClaim.ClaimType,
                AlwaysInclude = sc.AlwaysInclude,
                CustomMappingLogic = sc.CustomMappingLogic
            })
            .ToListAsync();

        return Ok(new
        {
            scopeId,
            scopeName,
            claims = updatedClaims,
            message = "Scope claims updated successfully."
        });
    }

    #endregion

    #region DTOs

    public sealed class ClientSummary
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Type { get; set; } = string.Empty; // public | confidential
        public string ApplicationType { get; set; } = string.Empty; // web | native
        public string ConsentType { get; set; } = string.Empty;
        public int RedirectUrisCount { get; set; }
    }

    public record CreateClientRequest(
        string ClientId,
        string? ClientSecret,
        string? DisplayName,
        string? ApplicationType,  // web, native
        string? Type,  // public, confidential
        string? ConsentType,
        List<string>? RedirectUris,
        List<string>? PostLogoutRedirectUris,
        List<string>? Permissions
    );

    public record UpdateClientRequest(
        string? ClientId,
        string? ClientSecret,
        string? DisplayName,
        string? Type,
        string? ConsentType,
        List<string>? RedirectUris,
        List<string>? PostLogoutRedirectUris,
        List<string>? Permissions
    );

    public record CreateScopeRequest(
        string Name,
        string? DisplayName,
        string? Description,
        List<string>? Resources
    );

    public record UpdateScopeRequest(
        string? Name,
        string? DisplayName,
        string? Description,
        List<string>? Resources
    );

    public sealed class DashboardStatsDto
    {
        public int TotalClients { get; set; }
        public int TotalScopes { get; set; }
        public int TotalUsers { get; set; }
    }

    public sealed class ClaimDefinitionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ClaimType { get; set; } = string.Empty;
        public string UserPropertyPath { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsStandard { get; set; }
        public bool IsRequired { get; set; }
        public int ScopeCount { get; set; }
    }

    public record CreateClaimRequest(
        string Name,
        string? DisplayName,
        string? Description,
        string ClaimType,
        string? UserPropertyPath,
        string? DataType,
        bool? IsRequired
    );

    public record UpdateClaimRequest(
        string? DisplayName,
        string? Description,
        string? ClaimType,
        string? UserPropertyPath,
        string? DataType,
        bool? IsRequired
    );

    public sealed class ScopeClaimDto
    {
        public int Id { get; set; }
        public string ScopeId { get; set; } = string.Empty;
        public string ScopeName { get; set; } = string.Empty;
        public int ClaimId { get; set; }
        public string ClaimName { get; set; } = string.Empty;
        public string ClaimDisplayName { get; set; } = string.Empty;
        public string ClaimType { get; set; } = string.Empty;
        public bool AlwaysInclude { get; set; }
        public string? CustomMappingLogic { get; set; }
    }

    public record UpdateScopeClaimsRequest(
        List<int>? ClaimIds
    );

    #endregion

    #region User Management

    /// <summary>
    /// Get users with server-side paging, filtering and sorting.
    /// </summary>
    /// <param name="skip">Number of items to skip (default: 0)</param>
    /// <param name="take">Number of items to take (default: 25)</param>
    /// <param name="search">Optional search string matched against email/name (case-insensitive)</param>
    /// <param name="role">Optional role filter</param>
    /// <param name="isActive">Optional active status filter</param>
    /// <param name="sortBy">Optional sort field: email, username, firstname, lastname, createdat (default: email)</param>
    /// <param name="sortDirection">Sort direction: asc or desc (default: asc)</param>
    [HttpGet("users")]
    [HasPermission(Core.Domain.Constants.Permissions.Users.Read)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = "email",
        [FromQuery] string? sortDirection = "asc")
    {
        try
        {
            var result = await _userManagementService.GetUsersAsync(
                skip, take, search, role, isActive, sortBy, sortDirection);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving users", details = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific user by ID.
    /// </summary>
    /// <param name="id">User ID</param>
    [HttpGet("users/{id}")]
    [HasPermission(DomainPermissions.Users.Read)]
    public async Task<IActionResult> GetUser(Guid id)
    {
        try
        {
            var user = await _userManagementService.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(new { error = "User not found" });

            return Ok(user);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Create a new user.
    /// </summary>
    /// <param name="request">User creation data</param>
    [HttpPost("users")]
    [HasPermission(DomainPermissions.Users.Create)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto request)
    {
        try
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? createdBy = currentUserId != null ? Guid.Parse(currentUserId) : null;

            var (success, userId, errors) = await _userManagementService.CreateUserAsync(request, createdBy);

            if (!success)
                return BadRequest(new { errors });

            var createdUser = await _userManagementService.GetUserByIdAsync(userId!.Value);
            return CreatedAtAction(nameof(GetUser), new { id = userId }, createdUser);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while creating the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing user.
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="request">User update data</param>
    [HttpPut("users/{id}")]
    [HasPermission(DomainPermissions.Users.Update)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto request)
    {
        try
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? modifiedBy = currentUserId != null ? Guid.Parse(currentUserId) : null;

            var (success, errors) = await _userManagementService.UpdateUserAsync(id, request, modifiedBy);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            var updatedUser = await _userManagementService.GetUserByIdAsync(id);
            return Ok(updatedUser);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while updating the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Deactivate a user (soft delete).
    /// </summary>
    /// <param name="id">User ID</param>
    [HttpPost("users/{id}/deactivate")]
    [HasPermission(DomainPermissions.Users.Delete)]
    public async Task<IActionResult> DeactivateUser(Guid id)
    {
        try
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? modifiedBy = currentUserId != null ? Guid.Parse(currentUserId) : null;

            var (success, errors) = await _userManagementService.DeactivateUserAsync(id, modifiedBy);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while deactivating the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Permanently delete a user (soft delete - won't show in UI).
    /// </summary>
    /// <param name="id">User ID</param>
    [HttpDelete("users/{id}")]
    [HasPermission(DomainPermissions.Users.Delete)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new { errors = new[] { "User not found" } });
            }

            // Soft delete: mark as deleted in database
            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId != null)
            {
                user.DeletedBy = Guid.Parse(currentUserId);
            }
            user.ModifiedAt = DateTime.UtcNow;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while deleting the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Reactivate a deactivated user.
    /// </summary>
    /// <param name="id">User ID</param>
    [HttpPost("users/{id}/reactivate")]
    [HasPermission(DomainPermissions.Users.Update)]
    public async Task<IActionResult> ReactivateUser(Guid id)
    {
        try
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? modifiedBy = currentUserId != null ? Guid.Parse(currentUserId) : null;

            var (success, errors) = await _userManagementService.ReactivateUserAsync(id, modifiedBy);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            var reactivatedUser = await _userManagementService.GetUserByIdAsync(id);
            return Ok(reactivatedUser);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while reactivating the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Assign roles to a user (replaces existing roles).
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="request">Role assignment data</param>
    [HttpPut("users/{id}/roles")]
    [HasPermission(DomainPermissions.Users.Update)]
    public async Task<IActionResult> AssignRoles(Guid id, [FromBody] AssignRolesRequest request)
    {
        try
        {
            var (success, errors) = await _userManagementService.AssignRolesAsync(id, request.Roles);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            var updatedUser = await _userManagementService.GetUserByIdAsync(id);
            return Ok(updatedUser);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while assigning roles", details = ex.Message });
        }
    }

    public record AssignRolesRequest(List<string> Roles);

    #endregion

    #region Role Management

    /// <summary>
    /// Get roles with server-side paging, optional search and sorting.
    /// </summary>
    /// <param name="skip">Number of items to skip (default: 0)</param>
    /// <param name="take">Number of items to take (default: 25)</param>
    /// <param name="search">Optional search string matched against name/description (case-insensitive)</param>
    /// <param name="sortBy">Optional sort field: name, createdat (default: name)</param>
    /// <param name="sortDirection">Sort direction: asc or desc (default: asc)</param>
    [HttpGet("roles")]
    [HasPermission(DomainPermissions.Roles.Read)]
    public async Task<IActionResult> GetRoles(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = "name",
        [FromQuery] string? sortDirection = "asc")
    {
        try
        {
            var result = await _roleManagementService.GetRolesAsync(skip, take, search, sortBy, sortDirection);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving roles", details = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific role by ID.
    /// </summary>
    /// <param name="id">Role ID</param>
    [HttpGet("roles/{id}")]
    [HasPermission(DomainPermissions.Roles.Read)]
    public async Task<IActionResult> GetRole(Guid id)
    {
        try
        {
            var role = await _roleManagementService.GetRoleByIdAsync(id);
            if (role == null)
                return NotFound(new { error = "Role not found" });

            return Ok(role);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving the role", details = ex.Message });
        }
    }

    /// <summary>
    /// Create a new role.
    /// </summary>
    /// <param name="request">Role creation data</param>
    [HttpPost("roles")]
    [HasPermission(DomainPermissions.Roles.Create)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleDto request)
    {
        try
        {
            var (success, roleId, errors) = await _roleManagementService.CreateRoleAsync(request);

            if (!success)
                return BadRequest(new { errors });

            var createdRole = await _roleManagementService.GetRoleByIdAsync(roleId!.Value);
            return CreatedAtAction(nameof(GetRole), new { id = roleId }, createdRole);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while creating the role", details = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing role.
    /// </summary>
    /// <param name="id">Role ID</param>
    /// <param name="request">Role update data</param>
    [HttpPut("roles/{id}")]
    [HasPermission(DomainPermissions.Roles.Update)]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleDto request)
    {
        try
        {
            var (success, errors) = await _roleManagementService.UpdateRoleAsync(id, request);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            var updatedRole = await _roleManagementService.GetRoleByIdAsync(id);
            return Ok(updatedRole);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while updating the role", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete a role.
    /// </summary>
    /// <param name="id">Role ID</param>
    [HttpDelete("roles/{id}")]
    [HasPermission(DomainPermissions.Roles.Delete)]
    public async Task<IActionResult> DeleteRole(Guid id)
    {
        try
        {
            var (success, errors) = await _roleManagementService.DeleteRoleAsync(id);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while deleting the role", details = ex.Message });
        }
    }

    /// <summary>
    /// Get all available permissions.
    /// </summary>
    [HttpGet("roles/permissions")]
    [HasPermission(DomainPermissions.Roles.Read)]
    public async Task<IActionResult> GetAvailablePermissions()
    {
        try
        {
            var permissions = await _roleManagementService.GetAvailablePermissionsAsync();
            return Ok(permissions);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving permissions", details = ex.Message });
        }
    }

    /// <summary>
    /// Get current user's permissions for UI authorization
    /// </summary>
    [HttpGet("permissions/current")]
    public async Task<IActionResult> GetCurrentUserPermissions()
    {
        try
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "User not authenticated" });
            }

            // Check if user is admin
            var isAdmin = User.IsInRole(AuthConstants.Roles.Admin);
            
            if (isAdmin)
            {
                // Admin has all permissions
                return Ok(new
                {
                    isAdmin = true,
                    permissions = DomainPermissions.GetAll()
                });
            }

            // Get user's roles and their permissions
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            var allPermissions = new HashSet<string>();

            // Use RoleManager to get role details
            var roleManager = HttpContext.RequestServices.GetRequiredService<RoleManager<ApplicationRole>>();
            
            foreach (var roleName in userRoles)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
                {
                    var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim());
                    
                    foreach (var permission in rolePermissions)
                    {
                        allPermissions.Add(permission);
                    }
                }
            }

            return Ok(new
            {
                isAdmin = false,
                permissions = allPermissions.ToList()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving user permissions", details = ex.Message });
        }
    }

    #endregion
}