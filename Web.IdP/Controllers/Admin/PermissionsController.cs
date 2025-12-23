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
[AutoValidateAntiforgeryToken]
public class PermissionsController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public PermissionsController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Get current user's permissions for UI authorization.
    /// Returns all permissions if user is admin, or aggregated permissions from user's roles.
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

            // Check if user is admin
            var isAdmin = User.IsInRole(AuthConstants.Roles.Admin);
            
            if (isAdmin)
            {
                // Admin has all permissions
                return Ok(new
                {
                    isAdmin = true,
                    permissions = Permissions.GetAll(),
                    userId = userId
                });
            }

            // Get user's roles and their permissions
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            var allPermissions = new HashSet<string>();

            // Use RoleManager to get role details
            var roleManager = HttpContext.RequestServices.GetRequiredService<RoleManager<ApplicationRole>>();
            
            foreach (var roleName in userRoles)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
                {
                    var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim());
                    
                    foreach (var permission in rolePermissions)
                    {
                        allPermissions.Add(permission);
                    }
                }
            }

            return Ok(new
            {
                isAdmin = false,
                permissions = allPermissions.ToList(),
                userId = userId
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred while retrieving user permissions", details = ex.Message });
        }
    }
}
