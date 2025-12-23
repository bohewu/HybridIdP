using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// API Resource management endpoints for defining API resources that can be protected by the IdP.
/// API resources group related scopes and enable audience claims in access tokens.
/// </summary>
[ApiController]
[Route("api/admin/resources")]
[ApiAuthorize]
[AutoValidateAntiforgeryToken]
public class ApiResourcesController : ControllerBase
{
    private readonly IApiResourceService _apiResourceService;

    public ApiResourcesController(IApiResourceService apiResourceService)
    {
        _apiResourceService = apiResourceService;
    }

    /// <summary>
    /// Get all API resources with pagination, search, and sorting.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Scopes.Read)] // Using Scopes.Read permission as resources are scope-related
    public async Task<ActionResult> GetResources(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null)
    {
        var (items, totalCount) = await _apiResourceService.GetResourcesAsync(skip, take, search, sort);
        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Get a specific API resource by ID with full details including associated scopes.
    /// </summary>
    [HttpGet("{id}")]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> GetResource(int id)
    {
        var resource = await _apiResourceService.GetResourceByIdAsync(id);
        if (resource == null)
        {
            return NotFound(new { message = $"API resource with ID '{id}' not found." });
        }
        return Ok(resource);
    }

    /// <summary>
    /// Create a new API resource.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Scopes.Create)]
    public async Task<ActionResult> CreateResource([FromBody] CreateApiResourceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _apiResourceService.CreateResourceAsync(request);
            return CreatedAtAction(nameof(GetResource), new { id = result.Id }, new
            {
                id = result.Id,
                name = result.Name,
                displayName = result.DisplayName,
                message = "API resource created successfully."
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing API resource.
    /// </summary>
    [HttpPut("{id}")]
    [HasPermission(Permissions.Scopes.Update)]
    public async Task<ActionResult> UpdateResource(int id, [FromBody] UpdateApiResourceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var updated = await _apiResourceService.UpdateResourceAsync(id, request);
            if (!updated)
            {
                return NotFound(new { message = $"API resource with ID '{id}' not found or update failed." });
            }
            return Ok(new
            {
                id,
                message = "API resource updated successfully."
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete an API resource.
    /// </summary>
    [HttpDelete("{id}")]
    [HasPermission(Permissions.Scopes.Delete)]
    public async Task<ActionResult> DeleteResource(int id)
    {
        var deleted = await _apiResourceService.DeleteResourceAsync(id);
        if (!deleted)
        {
            return NotFound(new { message = $"API resource with ID '{id}' not found." });
        }
        return Ok(new { message = "API resource deleted successfully." });
    }

    /// <summary>
    /// Get all scopes associated with a specific API resource.
    /// </summary>
    [HttpGet("{id}/scopes")]
    [HasPermission(Permissions.Scopes.Read)]
    public async Task<ActionResult> GetResourceScopes(int id)
    {
        var scopes = await _apiResourceService.GetResourceScopesAsync(id);
        return Ok(new { scopes });
    }
}
