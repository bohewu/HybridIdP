using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Api;

/// <summary>
/// API controller for managing users.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserManagementService _userManagementService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISessionService _sessionService;

    public UsersController(
        IUserManagementService userManagementService,
        UserManager<ApplicationUser> userManager,
        ISessionService sessionService)
    {
        _userManagementService = userManagementService;
        _userManager = userManager;
        _sessionService = sessionService;
    }

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
    [HttpGet]
    [HasPermission(Permissions.Users.Read)]
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
    [HttpGet("{id}")]
    [HasPermission(Permissions.Users.Read)]
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
    [HttpPost]
    [HasPermission(Permissions.Users.Create)]
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
    [HttpPut("{id}")]
    [HasPermission(Permissions.Users.Update)]
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
    [HttpPost("{id}/deactivate")]
    [HasPermission(Permissions.Users.Delete)]
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
    [HttpDelete("{id}")]
    [HasPermission(Permissions.Users.Delete)]
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
    [HttpPost("{id}/reactivate")]
    [HasPermission(Permissions.Users.Update)]
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
    [HttpPut("{id}/roles")]
    [HasPermission(Permissions.Users.Update)]
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

    /// <summary>
    /// Request model for assigning roles to a user.
    /// </summary>
    public record AssignRolesRequest(List<string> Roles);

    /// <summary>
    /// List active sessions (authorizations) for a user.
    /// </summary>
    /// <param name="id">User ID</param>
    [HttpGet("{id}/sessions")]
    [HasPermission(Permissions.Users.Read)]
    public async Task<IActionResult> ListSessions(Guid id)
    {
        try
        {
            var sessions = await _sessionService.ListSessionsAsync(id);
            return Ok(sessions);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving sessions", details = ex.Message });
        }
    }

    /// <summary>
    /// Revoke a specific session for a user.
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="authorizationId">Authorization ID</param>
    [HttpPost("{id}/sessions/{authorizationId}/revoke")]
    [HasPermission(Permissions.Users.Update)]
    public async Task<IActionResult> RevokeSession(Guid id, string authorizationId)
    {
        try
        {
            var success = await _sessionService.RevokeSessionAsync(id, authorizationId);
            if (!success)
            {
                return NotFound(new { error = "Authorization not found or not owned by user" });
            }
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while revoking the session", details = ex.Message });
        }
    }

    /// <summary>
    /// Revoke all sessions for a user.
    /// </summary>
    /// <param name="id">User ID</param>
    [HttpPost("{id}/sessions/revoke-all")]
    [HasPermission(Permissions.Users.Update)]
    public async Task<IActionResult> RevokeAllSessions(Guid id)
    {
        try
        {
            var count = await _sessionService.RevokeAllSessionsAsync(id);
            return Ok(new { revoked = count });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while revoking all sessions", details = ex.Message });
        }
    }
}
