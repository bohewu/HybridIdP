using Core.Application;
using Core.Application.DTOs;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainPermissions = Core.Domain.Constants.Permissions;

namespace Web.IdP.Api;

/// <summary>
/// API controller for managing OIDC clients.
/// </summary>
[ApiController]
[Route("api/admin/clients")]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly IClientAllowedScopesService _allowedScopesService;

    public ClientsController(
        IClientService clientService,
        IClientAllowedScopesService allowedScopesService)
    {
        _clientService = clientService;
        _allowedScopesService = allowedScopesService;
    }

    /// <summary>
    /// Get OIDC clients with server-side paging, filtering and sorting.
    /// </summary>
    [HttpGet]
    [HasPermission(DomainPermissions.Clients.Read)]
    public async Task<IActionResult> GetClients(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] string? sort = null)
    {
        var (items, totalCount) = await _clientService.GetClientsAsync(skip, take, search, type, sort);
        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Get a specific OIDC client by ID.
    /// </summary>
    [HttpGet("{id}")]
    [HasPermission(DomainPermissions.Clients.Read)]
    public async Task<IActionResult> GetClient(string id)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return BadRequest(new { message = "Invalid client ID format." });
        }

        var client = await _clientService.GetClientByIdAsync(clientId);
        if (client == null)
        {
            return NotFound(new { message = $"Client with ID '{id}' not found." });
        }

        return Ok(new
        {
            id = client.Id,
            clientId = client.ClientId,
            displayName = client.DisplayName,
            type = client.Type,
            consentType = client.ConsentType,
            redirectUris = client.RedirectUris,
            postLogoutRedirectUris = client.PostLogoutRedirectUris,
            permissions = client.Permissions
        });
    }

    /// <summary>
    /// Create a new OIDC client.
    /// </summary>
    [HttpPost]
    [HasPermission(DomainPermissions.Clients.Create)]
    public async Task<IActionResult> CreateClient([FromBody] CreateClientRequest request)
    {
        try
        {
            var response = await _clientService.CreateClientAsync(request);
            return CreatedAtAction(nameof(GetClient), new { id = response.Id }, new
            {
                id = response.Id,
                clientId = response.ClientId,
                displayName = response.DisplayName,
                message = "Client created successfully.",
                clientSecret = response.ClientSecret
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing OIDC client.
    /// </summary>
    [HttpPut("{id}")]
    [HasPermission(DomainPermissions.Clients.Update)]
    public async Task<IActionResult> UpdateClient(string id, [FromBody] UpdateClientRequest request)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return BadRequest(new { message = "Invalid client ID format." });
        }

        try
        {
            await _clientService.UpdateClientAsync(clientId, request);
            return Ok(new
            {
                id,
                message = "Client updated successfully."
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete an OIDC client.
    /// </summary>
    [HttpDelete("{id}")]
    [HasPermission(DomainPermissions.Clients.Delete)]
    public async Task<IActionResult> DeleteClient(string id)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return BadRequest(new { message = "Invalid client ID format." });
        }

        try
        {
            await _clientService.DeleteClientAsync(clientId);
            return Ok(new { message = "Client deleted successfully." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Regenerate the secret for a confidential client.
    /// </summary>
    [HttpPost("{id}/regenerate-secret")]
    [HasPermission(DomainPermissions.Clients.Update)]
    public async Task<IActionResult> RegenerateSecret(string id)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return BadRequest(new { message = "Invalid client ID format." });
        }

        try
        {
            var newSecret = await _clientService.RegenerateSecretAsync(clientId);
            return Ok(new
            {
                message = "Client secret regenerated successfully.",
                clientSecret = newSecret
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get allowed scopes for a specific client.
    /// </summary>
    [HttpGet("{id}/scopes")]
    [HasPermission(DomainPermissions.Clients.Read)]
    public async Task<IActionResult> GetAllowedScopes(string id)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return BadRequest(new { message = "Invalid client ID format." });
        }

        var scopes = await _allowedScopesService.GetAllowedScopesAsync(clientId);
        return Ok(new { scopes });
    }

    /// <summary>
    /// Set allowed scopes for a specific client.
    /// </summary>
    [HttpPut("{id}/scopes")]
    [HasPermission(DomainPermissions.Clients.Update)]
    public async Task<IActionResult> SetAllowedScopes(string id, [FromBody] SetAllowedScopesRequest request)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return BadRequest(new { message = "Invalid client ID format." });
        }

        if (request.Scopes == null)
        {
            return BadRequest(new { message = "Scopes are required." });
        }

        try
        {
            await _allowedScopesService.SetAllowedScopesAsync(clientId, request.Scopes);
            return Ok(new { message = "Allowed scopes updated successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Validate requested scopes against client's allowed scopes.
    /// </summary>
    [HttpPost("{id}/scopes/validate")]
    [HasPermission(DomainPermissions.Clients.Read)]
    public async Task<IActionResult> ValidateScopes(string id, [FromBody] ValidateScopesRequest request)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return BadRequest(new { message = "Invalid client ID format." });
        }

        if (request.RequestedScopes == null)
        {
            return BadRequest(new { message = "RequestedScopes are required." });
        }

        var allowedScopes = await _allowedScopesService.ValidateRequestedScopesAsync(clientId, request.RequestedScopes);
        return Ok(new { allowedScopes });
    }
}

public class SetAllowedScopesRequest
{
    public List<string>? Scopes { get; set; }
}

public class ValidateScopesRequest
{
    public List<string>? RequestedScopes { get; set; }
}
