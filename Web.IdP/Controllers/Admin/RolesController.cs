using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// API controller for managing roles and role permissions.
/// </summary>
[ApiController]
[Route("api/admin/roles")]
[ApiAuthorize]
public class RolesController : ControllerBase
{
    private readonly IRoleManagementService _roleManagementService;

    public RolesController(IRoleManagementService roleManagementService)
    {
        _roleManagementService = roleManagementService;
    }

    /// <summary>
    /// Get roles with server-side paging, optional search and sorting.
    /// </summary>
    /// <param name="skip">Number of items to skip (default: 0)</param>
    /// <param name="take">Number of items to take (default: 25)</param>
    /// <param name="search">Optional search string matched against name/description (case-insensitive)</param>
    /// <param name="sortBy">Optional sort field: name, createdat (default: name)</param>
    /// <param name="sortDirection">Sort direction: asc or desc (default: asc)</param>
    [HttpGet]
    [HasPermission(Permissions.Roles.Read)]
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
    [HttpGet("{id}")]
    [HasPermission(Permissions.Roles.Read)]
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
    [HttpPost]
    [HasPermission(Permissions.Roles.Create)]
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
    [HttpPut("{id}")]
    [HasPermission(Permissions.Roles.Update)]
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
    [HttpDelete("{id}")]
    [HasPermission(Permissions.Roles.Delete)]
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
    [HttpGet("permissions")]
    [HasPermission(Permissions.Roles.Read)]
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
}
