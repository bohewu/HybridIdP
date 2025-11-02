using Core.Domain.Constants;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Api;

/// <summary>
/// Admin API controller for management operations.
/// All endpoints require the Admin role.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AuthConstants.Roles.Admin)]
public class AdminController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;

    public AdminController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager)
    {
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
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

    #region OIDC Clients

    /// <summary>
    /// Get all OIDC clients.
    /// </summary>
    [HttpGet("clients")]
    public async Task<IActionResult> GetClients()
    {
        var clients = new List<object>();
        
        await foreach (var application in _applicationManager.ListAsync())
        {
            // Retrieve additional details for list display
            var clientId = await _applicationManager.GetClientIdAsync(application);
            var displayName = await _applicationManager.GetDisplayNameAsync(application);
            var clientType = await _applicationManager.GetClientTypeAsync(application);
            var consentType = await _applicationManager.GetConsentTypeAsync(application);
            var applicationType = await _applicationManager.GetApplicationTypeAsync(application);
            var redirectUris = await _applicationManager.GetRedirectUrisAsync(application);

            clients.Add(new
            {
                id = await _applicationManager.GetIdAsync(application),
                clientId,
                displayName,
                type = clientType,
                applicationType,
                consentType,
                redirectUrisCount = redirectUris.Count()
            });
        }

        return Ok(clients);
    }

    /// <summary>
    /// Get a specific OIDC client by ID.
    /// </summary>
    [HttpGet("clients/{id}")]
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
            descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(Permissions.Endpoints.Token);
            descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
            descriptor.Permissions.Add(Permissions.Scopes.Email);
            descriptor.Permissions.Add(Permissions.Scopes.Profile);
            descriptor.Permissions.Add($"{Permissions.Prefixes.Scope}{AuthConstants.Scopes.OpenId}");
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
    public async Task<IActionResult> DeleteScope(string id)
    {
        var scope = await _scopeManager.FindByIdAsync(id);
        if (scope == null)
        {
            return NotFound(new { message = $"Scope with ID '{id}' not found." });
        }

        await _scopeManager.DeleteAsync(scope);

        return Ok(new { message = "Scope deleted successfully." });
    }

    #endregion

    #region DTOs

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

    #endregion
}
