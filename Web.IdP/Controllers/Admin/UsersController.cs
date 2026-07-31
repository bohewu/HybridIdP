using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Web.IdP;
using Web.IdP.Services;

using Web.IdP.Attributes;
using AspNetCoreAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

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
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ISessionService _sessionService;
    private readonly ILoginHistoryService _loginHistoryService;
    private readonly IApplicationDbContext _dbContext;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IImpersonationService _impersonationService;
    private readonly AspNetCoreAuthorizationService _authorizationService;
    private readonly ILogger<UsersController> _logger;
    private readonly PrivilegedRoleProtectionOptions _privilegedRoleProtectionOptions;

    public UsersController(
        IUserManagementService userManagementService,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ISessionService sessionService,
        ILoginHistoryService loginHistoryService,
        IApplicationDbContext dbContext,
        IStringLocalizer<SharedResource> localizer,
        IImpersonationService impersonationService,
        AspNetCoreAuthorizationService authorizationService,
        IOptions<PrivilegedRoleProtectionOptions> privilegedRoleProtectionOptions,
        ILogger<UsersController> logger)
    {
        _userManagementService = userManagementService;
        _userManager = userManager;
        _roleManager = roleManager;
        _sessionService = sessionService;
        _loginHistoryService = loginHistoryService;
        _dbContext = dbContext;
        _localizer = localizer;
        _impersonationService = impersonationService;
        _authorizationService = authorizationService;
        _privilegedRoleProtectionOptions = privilegedRoleProtectionOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Get users with server-side paging, filtering and sorting.
    /// </summary>
    /// <param name="role">Optional role filter</param>
    /// <param name="isActive">Optional active status filter</param>
    /// <param name="sortBy">Optional sort field: email, username, firstname, lastname, department, createdat (default: createdat)</param>
    /// <param name="sortDirection">Sort direction: asc or desc (default: desc)</param>
    [HttpGet]
    [HasPermission(Permissions.Users.Read)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = "createdat",
        [FromQuery] string? sortDirection = "desc",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _userManagementService.GetUsersAsync(
                skip, take, search, role, isActive, sortBy, sortDirection, cancellationToken);
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
    public async Task<IActionResult> GetUser(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userManagementService.GetUserByIdAsync(id, cancellationToken);
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
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.Roles.Count > 0)
            {
                var roleMutationPolicyResult = await RequireRoleUpdatePermissionAsync();
                if (roleMutationPolicyResult != null)
                {
                    return roleMutationPolicyResult;
                }
            }

            var privilegedRoleCreatePolicyResult = await EnforcePrivilegedRoleCreationPolicyAsync(request.Roles);
            if (privilegedRoleCreatePolicyResult != null)
            {
                return privilegedRoleCreatePolicyResult;
            }

            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? createdBy = currentUserId != null ? Guid.Parse(currentUserId) : null;

            var (success, userId, errors) = await _userManagementService.CreateUserAsync(request, createdBy, cancellationToken);

            if (!success)
                return BadRequest(new { errors });

            var createdUser = await _userManagementService.GetUserByIdAsync(userId!.Value, cancellationToken);
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
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var targetUser = await _userManagementService.GetUserByIdAsync(id, cancellationToken);
            if (targetUser == null)
            {
                return NotFound(new { errors = new[] { "User not found" } });
            }

            var requestedRoleSet = request.Roles.ToHashSet(StringComparer.Ordinal);
            var currentRoleSet = targetUser.Roles.ToHashSet(StringComparer.Ordinal);
            var rolesChanged = !currentRoleSet.SetEquals(requestedRoleSet);

            if (rolesChanged)
            {
                var roleMutationPolicyResult = await RequireRoleUpdatePermissionAsync();
                if (roleMutationPolicyResult != null)
                {
                    return roleMutationPolicyResult;
                }

                var privilegedRolePolicyResult = await EnforcePrivilegedRoleAssignmentPolicyAsync(id, request.Roles);
                if (privilegedRolePolicyResult != null)
                {
                    return privilegedRolePolicyResult;
                }
            }

            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? modifiedBy = currentUserId != null ? Guid.Parse(currentUserId) : null;

            var (success, errors) = rolesChanged
                ? await _userManagementService.UpdateUserAsync(id, request, modifiedBy, cancellationToken)
                : await _userManagementService.UpdateUserWithoutRolesAsync(id, request, modifiedBy, cancellationToken);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            var updatedUser = await _userManagementService.GetUserByIdAsync(id, cancellationToken);
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
    public async Task<IActionResult> DeactivateUser(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? modifiedBy = currentUserId != null ? Guid.Parse(currentUserId) : null;

            var (success, errors) = await _userManagementService.DeactivateUserAsync(id, modifiedBy, cancellationToken);

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
    /// Permanently delete a user (soft delete + clear external logins for JIT re-provisioning).
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

            // Step 1: Soft delete - mark as deleted in database
            user.IsDeleted = true;
            user.IsActive = false;
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

            // Step 2: Remove external logins (allows JIT to create new user on next login)
            var logins = await _userManager.GetLoginsAsync(user);
            foreach (var login in logins)
            {
                await _userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            }

            // Step 3: Update security stamp to invalidate existing sessions
            await _userManager.UpdateSecurityStampAsync(user);

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
    public async Task<IActionResult> ReactivateUser(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? modifiedBy = currentUserId != null ? Guid.Parse(currentUserId) : null;

            var (success, errors) = await _userManagementService.ReactivateUserAsync(id, modifiedBy, cancellationToken);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            var reactivatedUser = await _userManagementService.GetUserByIdAsync(id, cancellationToken);
            return Ok(reactivatedUser);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while reactivating the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Unlock a locked-out user account and reset failed access count.
    /// </summary>
    /// <param name="id">User ID</param>
    [HttpPost("{id}/unlock")]
    [HasPermission(Permissions.Users.Update)]
    public async Task<IActionResult> UnlockUser(Guid id)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);

            return Ok(new
            {
                message = "User unlocked successfully.",
                userId = id
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while unlocking the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Assign roles to a user (replaces existing roles).
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="request">Role assignment data</param>
    [HttpPut("{id}/roles")]
    [HasPermission(Permissions.Users.Update)]
    public async Task<IActionResult> AssignRoles(Guid id, [FromBody] AssignRolesRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var roleMutationPolicyResult = await RequireRoleUpdatePermissionAsync();
            if (roleMutationPolicyResult != null)
            {
                return roleMutationPolicyResult;
            }

            var privilegedRolePolicyResult = await EnforcePrivilegedRoleAssignmentPolicyAsync(id, request.Roles);
            if (privilegedRolePolicyResult != null)
            {
                return privilegedRolePolicyResult;
            }

            var (success, errors) = await _userManagementService.AssignRolesAsync(id, request.Roles, cancellationToken);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            var updatedUser = await _userManagementService.GetUserByIdAsync(id, cancellationToken);
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
    public async Task<IActionResult> AssignRolesByIds(Guid id, [FromBody] AssignRolesByIdRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var roleMutationPolicyResult = await RequireRoleUpdatePermissionAsync();
            if (roleMutationPolicyResult != null)
            {
                return roleMutationPolicyResult;
            }

            var requestedRoleNames = new List<string>();
            foreach (var roleId in request.RoleIds)
            {
                var role = await _roleManager.FindByIdAsync(roleId.ToString());
                if (!string.IsNullOrWhiteSpace(role?.Name))
                {
                    requestedRoleNames.Add(role.Name!);
                }
            }

            var privilegedRolePolicyResult = await EnforcePrivilegedRoleAssignmentPolicyAsync(id, requestedRoleNames);
            if (privilegedRolePolicyResult != null)
            {
                return privilegedRolePolicyResult;
            }

            var (success, errors) = await _userManagementService.AssignRolesByIdAsync(id, request.RoleIds, cancellationToken);

            if (!success)
            {
                if (errors.Any(e => e.Contains("not found")))
                    return NotFound(new { errors });
                return BadRequest(new { errors });
            }

            var updatedUser = await _userManagementService.GetUserByIdAsync(id, cancellationToken);
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
    /// Get assigned app roles for a user and client.
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="clientId">Client ID</param>
    [HttpGet("{id}/app-roles/{clientId}")]
    [HasPermission(Permissions.Users.Read)]
    public async Task<IActionResult> GetUserAppRoles(Guid id, string clientId, CancellationToken cancellationToken = default)
    {
        try
        {
            var roles = await _userManagementService.GetUserAppRolesAsync(id, clientId, cancellationToken);
            return Ok(roles);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving app roles", details = ex.Message });
        }
    }

    /// <summary>
    /// Assign app roles to a user for a specific client.
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="clientId">Client ID</param>
    /// <param name="request">List of role names</param>
    [HttpPut("{id}/app-roles/{clientId}")]
    [HasPermission(Permissions.Users.Update)]
    public async Task<IActionResult> AssignUserAppRoles(Guid id, string clientId, [FromBody] List<string> request, CancellationToken cancellationToken = default)
    {
        try
        {
            var (success, errors) = await _userManagementService.AssignUserAppRolesAsync(id, clientId, request, cancellationToken);

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
            return StatusCode(500, new { error = "An error occurred while assigning app roles", details = ex.Message });
        }
    }

    /// <summary>
    /// List sessions (authorizations) for a user with optional paging.
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="page">1-based page index (default: 1)</param>
    /// <param name="pageSize">Items per page (default: 10)</param>
    [HttpGet("{id}/sessions")]
    [HasPermission(Permissions.Users.Read)]
    public async Task<IActionResult> ListSessions(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            var all = (await _sessionService.ListSessionsAsync(id, cancellationToken)).ToList();
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
    public async Task<IActionResult> RevokeSession(Guid id, string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Allow if [HasPermission] passed (Admins) or if Self (if checks allowed looser access, but current attribute is strict)
            // For M2M, IsInRole("Admin") is false, but they have the scope. 
            // Since [HasPermission(Users.Update)] guards this, we can trust the caller has permission.
            // Self-revocation logic would require relaxed attribute, but for now assuming Admin-only or M2M-Admin.
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var success = await _sessionService.RevokeSessionAsync(id, authorizationId, cancellationToken);
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
    public async Task<IActionResult> RevokeAllSessions(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            // [HasPermission] already validated access.
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var count = await _sessionService.RevokeAllSessionsAsync(id, cancellationToken);
            
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
                if (error == "Cannot impersonate another administrator")
                {
                    return BadRequest(new { error = "系統管理員無法被模擬登入。" });
                }
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

    private async Task<IActionResult?> RequireRoleUpdatePermissionAsync()
    {
        var authorizationResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            Permissions.Roles.Update);

        return authorizationResult.Succeeded ? null : Forbid();
    }

    private async Task<IActionResult?> EnforcePrivilegedRoleCreationPolicyAsync(IEnumerable<string>? requestedRoles)
    {
        if (!ContainsProtectedRole(requestedRoles))
        {
            return null;
        }

        if (_privilegedRoleProtectionOptions.RequireOperatorMfaForPrivilegedRoleAssignment)
        {
            var operatorUser = await GetCurrentOperatorAsync();
            if (operatorUser == null)
            {
                return Unauthorized();
            }

            if (!HasCompletedMfaInCurrentSession())
            {
                return BadRequest(new
                {
                    errors = new[]
                    {
                        "Operator must complete MFA in the current session before assigning privileged roles."
                    }
                });
            }
        }

        if (_privilegedRoleProtectionOptions.RequireTargetMfaForPrivilegedRoleAssignment)
        {
            return BadRequest(new
            {
                errors = new[]
                {
                    "Privileged roles cannot be assigned during user creation when target MFA enforcement is enabled. Create the user, complete MFA enrollment, then assign the privileged role."
                }
            });
        }

        return null;
    }

    private async Task<IActionResult?> EnforcePrivilegedRoleAssignmentPolicyAsync(Guid targetUserId, IEnumerable<string>? requestedRoles)
    {
        if (!ContainsProtectedRole(requestedRoles))
        {
            return null;
        }

        var targetUser = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (targetUser == null)
        {
            return NotFound(new { errors = new[] { "User not found" } });
        }

        if (_privilegedRoleProtectionOptions.RequireOperatorMfaForPrivilegedRoleAssignment)
        {
            var operatorUser = await GetCurrentOperatorAsync();
            if (operatorUser == null)
            {
                return Unauthorized();
            }

            if (!HasCompletedMfaInCurrentSession())
            {
                return BadRequest(new
                {
                    errors = new[]
                    {
                        "Operator must complete MFA in the current session before assigning privileged roles."
                    }
                });
            }
        }

        if (_privilegedRoleProtectionOptions.RequireTargetMfaForPrivilegedRoleAssignment &&
            !await HasAnyMfaMethodEnabledAsync(targetUser))
        {
            return BadRequest(new
            {
                errors = new[]
                {
                    "Target user must enable MFA before being assigned privileged roles."
                }
            });
        }

        return null;
    }

    private bool ContainsProtectedRole(IEnumerable<string>? requestedRoles)
    {
        if (requestedRoles == null)
        {
            return false;
        }

        var protectedRoles = (_privilegedRoleProtectionOptions.ProtectedRoles ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (protectedRoles.Count == 0)
        {
            return false;
        }

        return requestedRoles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Any(role => protectedRoles.Contains(role));
    }

    private async Task<ApplicationUser?> GetCurrentOperatorAsync()
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return null;
        }

        return await _userManager.FindByIdAsync(currentUserId);
    }

    private bool HasCompletedMfaInCurrentSession()
    {
        var authenticationMethods = User.Claims
            .Where(claim =>
                claim.Type == AuthConstants.ClaimTypes.Amr ||
                claim.Type == AuthConstants.ClaimTypes.AuthenticationMethod)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasOtp = authenticationMethods.Contains(AuthConstants.Amr.Otp);
        var hasMfa = authenticationMethods.Contains(AuthConstants.Amr.Mfa);
        var hasHardwareKey = authenticationMethods.Contains(AuthConstants.Amr.HardwareKey);

        if (hasHardwareKey && !_privilegedRoleProtectionOptions.CountPasskeyAsMfa && !hasOtp)
        {
            return false;
        }

        return hasMfa ||
               (_privilegedRoleProtectionOptions.CountPasskeyAsMfa && hasHardwareKey);
    }

    private async Task<bool> HasAnyMfaMethodEnabledAsync(ApplicationUser user)
    {
        if (user.TwoFactorEnabled || user.EmailMfaEnabled)
        {
            return true;
        }

        if (!_privilegedRoleProtectionOptions.CountPasskeyAsMfa)
        {
            return false;
        }

        return await _dbContext.UserCredentials.AnyAsync(c => c.UserId == user.Id);
    }
    }
