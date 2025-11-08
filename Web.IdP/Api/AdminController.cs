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

    // Moved to DashboardController: GET api/admin/dashboard/stats

    #region OIDC Clients
    // Moved to ClientsController: GET/GET{id}/POST/PUT{id}/DELETE{id} api/admin/clients
    #endregion

    #region OIDC Scopes
    // Moved to ScopesController: GET/POST/PUT/DELETE api/admin/scopes
    #endregion

    #region User Claims Management
    // Moved to ClaimsController: GET/POST/PUT/DELETE api/admin/claims
    #endregion

    #region Scope-to-Claims Mapping
    // Moved to ScopeClaimsController: GET/PUT api/admin/scopes/{scopeId}/claims
    #endregion

    #region DTOs
    // All DTOs moved to Core.Application.DTOs:
    // - DashboardStatsDto
    // - ClaimDefinitionDto, CreateClaimRequest, UpdateClaimRequest (ClaimDtos.cs)
    // - ScopeClaimDto, UpdateScopeClaimsRequest (ScopeClaimDtos.cs)
    // - ScopeSummary, CreateScopeRequest, UpdateScopeRequest (ScopeDtos.cs)
    // - ClientSummary, CreateClientRequest, UpdateClientRequest (ClientDtos.cs)
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

    // Moved to PermissionsController: GET api/admin/permissions/current

    #endregion
}