using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Management endpoints for Localization Resources (Translations).
/// </summary>
[ApiController]
[Route("api/admin/localization")]
[Authorize]
public class LocalizationController : ControllerBase
{
    private readonly ILocalizationManagementService _service;

    public LocalizationController(ILocalizationManagementService service)
    {
        _service = service;
    }

    /// <summary>
    /// Get all localization resources with filtering and pagination.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Localization.Read)]
    public async Task<ActionResult> GetResources(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null)
    {
        var (items, totalCount) = await _service.GetResourcesAsync(skip, take, search, sort);
        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Get a specific localization resource by ID.
    /// </summary>
    [HttpGet("{id}")]
    [HasPermission(Permissions.Localization.Read)]
    public async Task<ActionResult> GetResource(int id)
    {
        var resource = await _service.GetResourceByIdAsync(id);
        if (resource == null)
        {
            return NotFound(new { message = $"Resource with ID '{id}' not found." });
        }
        return Ok(resource);
    }

    /// <summary>
    /// Create a new localization resource.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Localization.Create)]
    public async Task<ActionResult> CreateResource([FromBody] CreateResourceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _service.CreateResourceAsync(request);
            return CreatedAtAction(nameof(GetResource), new { id = result.Id }, new
            {
                id = result.Id,
                key = result.Key,
                culture = result.Culture,
                message = "Resource created successfully."
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing localization resource.
    /// </summary>
    [HttpPut("{id}")]
    [HasPermission(Permissions.Localization.Update)]
    public async Task<ActionResult> UpdateResource(int id, [FromBody] UpdateResourceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var updated = await _service.UpdateResourceAsync(id, request);
        if (!updated)
        {
            return NotFound(new { message = $"Resource with ID '{id}' not found." });
        }

        return Ok(new { message = "Resource updated successfully." });
    }

    /// <summary>
    /// Delete a localization resource.
    /// </summary>
    [HttpDelete("{id}")]
    [HasPermission(Permissions.Localization.Delete)]
    public async Task<ActionResult> DeleteResource(int id)
    {
        var deleted = await _service.DeleteResourceAsync(id);
        if (!deleted)
        {
            return NotFound(new { message = $"Resource with ID '{id}' not found." });
        }

        return Ok(new { message = "Resource deleted successfully." });
    }
}
