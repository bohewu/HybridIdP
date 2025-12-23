using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Extensions.Localization;
using Web.IdP;
using Web.IdP.Services;

using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// API controller for managing users.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public class UsersController : ControllerBase
{
    private readonly IUserManagementService _userManagementService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISessionService _sessionService;
    private readonly ILoginHistoryService _loginHistoryService;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IImpersonationService _impersonationService;

    public UsersController(
        IUserManagementService userManagementService,
        UserManager<ApplicationUser> userManager,
        ISessionService sessionService,
        ILoginHistoryService loginHistoryService,
        IStringLocalizer<SharedResource> localizer,
        IImpersonationService impersonationService)
    {
        _userManagementService = userManagementService;
        _userManager = userManager;
        _sessionService = sessionService;
        _loginHistoryService = loginHistoryService;
        _localizer = localizer;
        _impersonationService = impersonationService;
    }

    /// <summary>
    /// Get users with server-side paging, filtering and sorting.
    /// </summary>
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
    /// Assign roles to a user by role IDs (replaces existing roles).
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="request">Role assignment data with role IDs</param>
    [HttpPut("{id}/roles/ids")]
    [HasPermission(Permissions.Users.Update)]
    public async Task<IActionResult> AssignRolesByIds(Guid id, [FromBody] AssignRolesByIdRequest request)
    {
        try
        {
            var (success, errors) = await _userManagementService.AssignRolesByIdAsync(id, request.RoleIds);

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
    /// Request model for assigning roles to a user by role IDs.
    /// </summary>
    public record AssignRolesByIdRequest(List<Guid> RoleIds);

    /// <summary>
    /// List sessions (authorizations) for a user with optional paging.
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="page">1-based page index (default: 1)</param>
    /// <param name="pageSize">Items per page (default: 10)</param>
    [HttpGet("{id}/sessions")]
    [HasPermission(Permissions.Users.Read)]
    public async Task<IActionResult> ListSessions(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            var all = (await _sessionService.ListSessionsAsync(id)).ToList();
            var total = all.Count;
            var pages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
            if (page > pages) page = pages;
            var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Ok(new { items, page, pageSize, total, pages });
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
            // Allow if [HasPermission] passed (Admins) or if Self (if checks allowed looser access, but current attribute is strict)
            // For M2M, IsInRole("Admin") is false, but they have the scope. 
            // Since [HasPermission(Users.Update)] guards this, we can trust the caller has permission.
            // Self-revocation logic would require relaxed attribute, but for now assuming Admin-only or M2M-Admin.
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var success = await _sessionService.RevokeSessionAsync(id, authorizationId);
            if (!success)
            {
                return NotFound(new { error = "Authorization not found or not owned by user" });
            }

            // If the current user revoked their own current authorization, sign them out so the cookie is invalidated
            try
            {
                // reuse previously-resolved currentUserId
                // var currentUserId already resolved above
                if (string.Equals(currentUserId, id.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    // Look for a claim that contains the authorization id - OpenIddict sets an authorization claim when signing in
                    var currentAuth = User.Claims.FirstOrDefault(c =>
                        c.Type.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        c.Type.IndexOf("authorization_id", StringComparison.OrdinalIgnoreCase) >= 0)?.Value;

                    if (!string.IsNullOrEmpty(currentAuth) && currentAuth == authorizationId)
                    {
                        // Sign-out current HTTP context - best-effort
                        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    }
                }
            }
            catch
            {
                // ignore sign-out errors - revocation already succeeded
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
            // [HasPermission] already validated access.
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var count = await _sessionService.RevokeAllSessionsAsync(id);
            
            // Force invalidation of existing cookies by updating Security Stamp
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user != null)
            {
                await _userManager.UpdateSecurityStampAsync(user);
            }

            return Ok(new { revoked = count });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while revoking all sessions", details = ex.Message });
        }
    }

    /// <summary>
    /// Get login history for a user.
    /// </summary>
    /// <param name="id">The user ID</param>
    /// <param name="count">Number of recent logins to retrieve</param>
    [HttpGet("{id}/login-history")]
    [HasPermission(Permissions.Users.Read)]
    public async Task<IActionResult> GetLoginHistory(Guid id, [FromQuery] int count = 10)
    {
        try
        {
            var history = await _loginHistoryService.GetLoginHistoryAsync(id, count);
            return Ok(history);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving login history", details = ex.Message });
        }
    }

    /// <summary>
    /// Approve an abnormal login attempt, allowing the IP address for future logins.
    /// </summary>
    /// <param name="id">The user ID</param>
    /// <param name="loginHistoryId">The login history entry ID to approve</param>
    [HttpPost("{id}/login-history/{loginHistoryId}/approve")]
    [HasPermission(Permissions.Users.Update)]
    public async Task<IActionResult> ApproveAbnormalLogin(Guid id, int loginHistoryId)
    {
        try
        {
            var result = await _loginHistoryService.ApproveAbnormalLoginAsync(loginHistoryId);
            if (!result)
            {
                return NotFound(new { error = "Login history entry not found or not abnormal" });
            }
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while approving the abnormal login", details = ex.Message });
        }
    }

    /// <summary>
    /// Start impersonation of a user.
    /// </summary>
    /// <param name="id">User ID to impersonate</param>
    [HttpPost("{id}/impersonate")]
    [HasPermission(Permissions.Users.Impersonate)]
    public async Task<IActionResult> StartImpersonation(Guid id)
    {
        try
        {
            var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                   ?? User.GetClaim(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject);

            if (string.IsNullOrEmpty(currentUserIdStr) || !Guid.TryParse(currentUserIdStr, out var currentUserId))
            {
                return Unauthorized();
            }

            // Call Service
            var (success, principal, error) = await _impersonationService.StartImpersonationAsync(currentUserId, id);

            if (!success)
            {
                if (error == "User not found") return NotFound(new { error });
                if (error == "Cannot impersonate another administrator") return Forbid();
                return BadRequest(new { error });
            }

            // Issue the cookie
            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal!, new AuthenticationProperties
            {
                IsPersistent = false
            });

            // For response, we need target user email. 
            // The service returns Principal, we can get Name from it or we might change Service to return User object too?
            // The existing response wanted "targetUser: email".
            // The Principal has Name (which is username).
            // Let's look at the principal.Identity.Name.
            var targetUserName = principal!.Identity?.Name;

            return Ok(new { success = true, targetUser = targetUserName });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while starting impersonation", details = ex.Message });
        }
    }

    /// <summary>
    /// Reverts impersonation and restores the original identity.
    /// </summary>
    [HttpPost("stop-impersonation")]
    public async Task<IActionResult> StopImpersonation()
    {
        try
        {
            var (success, principal, error) = await _impersonationService.RevertImpersonationAsync(User);

            if (!success)
            {
                if (error == "Original user not found")
                {
                    // Force logout
                    await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    return Ok(new { message = "Original user not found, logged out." });
                }
                return BadRequest(new { error });
            }

            // Restore the cookie
            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal!, new AuthenticationProperties
            {
                IsPersistent = false
            });

            return Ok(new { message = "Impersonation stopped successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while stopping impersonation", details = ex.Message });
        }
    }

    /// <summary>
    /// Reset MFA for a user (admin action to force disable 2FA).
    /// </summary>
    /// <param name="id">User ID</param>
    [HttpPost("{id}/reset-mfa")]
    [HasPermission(Permissions.Users.Update)]
    public async Task<IActionResult> ResetMfa(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            // Disable TOTP 2FA
            await _userManager.SetTwoFactorEnabledAsync(user, false);
            // Reset authenticator key
            await _userManager.ResetAuthenticatorKeyAsync(user);
            
            // Also disable Email MFA
            user.EmailMfaEnabled = false;
            user.EmailMfaCode = null;
            user.EmailMfaCodeExpiry = null;
            await _userManager.UpdateAsync(user);

            return Ok(new { success = true, message = "MFA has been reset for the user" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while resetting MFA", details = ex.Message });
        }
    }
    }

