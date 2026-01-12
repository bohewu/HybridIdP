using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Web.IdP.Attributes;
using Web.IdP.Extensions;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// API Resource management endpoints for defining API resources that can be protected by the IdP.
/// API resources group related scopes and enable audience claims in access tokens.
/// </summary>
[ApiController]
[Route("api/admin/resources")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public class ApiResourcesController : ControllerBase
{
    private readonly IApiResourceService _apiResourceService;

    public ApiResourcesController(IApiResourceService apiResourceService)
    {
        _apiResourceService = apiResourceService;
    }

    /// <summary>
    /// Get all API resources with pagination, search, and sorting.
    /// Admin sees all resources with full edit rights.
    /// Application Manager sees all resources, but IsReadOnly is set for resources they don't own.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.ApiResources.Read)]
    public async Task<ActionResult> GetResources(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null)
    {
        Guid? viewerPersonId = User.IsAdmin() ? null : User.GetCurrentPersonId();
        var (items, totalCount) = await _apiResourceService.GetResourcesAsync(skip, take, search, sort, viewerPersonId);
        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Get a specific API resource by ID with full details including associated scopes.
    /// </summary>
    [HttpGet("{id}")]
    [HasPermission(Permissions.ApiResources.Read)]
    public async Task<ActionResult> GetResource(int id)
    {
        Guid? viewerPersonId = User.IsAdmin() ? null : User.GetCurrentPersonId();
        var resource = await _apiResourceService.GetResourceByIdAsync(id, viewerPersonId);
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
    [HasPermission(Permissions.ApiResources.Create)]
    public async Task<ActionResult> CreateResource([FromBody] CreateApiResourceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var personId = User.GetCurrentPersonId();
            var result = await _apiResourceService.CreateResourceAsync(request, personId);
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
    [HasPermission(Permissions.ApiResources.Update)]
    public async Task<ActionResult> UpdateResource(int id, [FromBody] UpdateApiResourceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            Guid? viewerPersonId = User.IsAdmin() ? null : User.GetCurrentPersonId();
            var updated = await _apiResourceService.UpdateResourceAsync(id, request, viewerPersonId);
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
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    /// <summary>
    /// Delete an API resource.
    /// </summary>
    [HttpDelete("{id}")]
    [HasPermission(Permissions.ApiResources.Delete)]
    public async Task<ActionResult> DeleteResource(int id)
    {
        try
        {
            Guid? viewerPersonId = User.IsAdmin() ? null : User.GetCurrentPersonId();
            var deleted = await _apiResourceService.DeleteResourceAsync(id, viewerPersonId);
            if (!deleted)
            {
                return NotFound(new { message = $"API resource with ID '{id}' not found." });
            }
            return Ok(new { message = "API resource deleted successfully." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    /// <summary>
    /// Get all scopes associated with a specific API resource.
    /// </summary>
    [HttpGet("{id}/scopes")]
    [HasPermission(Permissions.ApiResources.Read)]
    public async Task<ActionResult> GetResourceScopes(int id)
    {
        var scopes = await _apiResourceService.GetResourceScopesAsync(id);
        return Ok(new { scopes });
    }


}
