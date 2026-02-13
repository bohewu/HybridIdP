using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Permissions endpoints split from AdminController.
/// Routes preserved: api/admin/permissions/*
/// </summary>
[ApiController]
[Route("api/admin/permissions")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public class PermissionsController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public PermissionsController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    /// <summary>
    /// Get current user's permissions for UI authorization.
    /// Uses active_role when present; otherwise falls back to role aggregation.
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult> GetCurrent()
    {
        try
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "User not authenticated" });
            }

            // Resolve user roles from store to align UI authorization with backend handlers.
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            var activeRole = User.FindFirst("active_role")?.Value;
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var effectiveIsAdmin = false;

            if (!string.IsNullOrWhiteSpace(activeRole))
            {
                if (string.Equals(activeRole, AuthConstants.Roles.Admin, StringComparison.OrdinalIgnoreCase))
                {
                    effectiveIsAdmin = true;
                    permissions.UnionWith(Permissions.GetAll());
                }
                else
                {
                    var activeRoleEntity = await _roleManager.FindByNameAsync(activeRole);
                    if (activeRoleEntity != null && !string.IsNullOrWhiteSpace(activeRoleEntity.Permissions))
                    {
                        permissions.UnionWith(
                            activeRoleEntity.Permissions
                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => p.Trim()));
                    }
                }
            }
            else
            {
                // Backward-compatible fallback when active_role is not present.
                effectiveIsAdmin = userRoles.Contains(AuthConstants.Roles.Admin, StringComparer.OrdinalIgnoreCase);
                if (effectiveIsAdmin)
                {
                    permissions.UnionWith(Permissions.GetAll());
                }
                else
                {
                    foreach (var roleName in userRoles)
                    {
                        var role = await _roleManager.FindByNameAsync(roleName);
                        if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
                        {
                            permissions.UnionWith(
                                role.Permissions
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(p => p.Trim()));
                        }
                    }
                }
            }

            return Ok(new
            {
                isAdmin = effectiveIsAdmin,
                permissions = permissions.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
                userId,
                activeRole
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving user permissions", details = ex.Message });
        }
    }
}
