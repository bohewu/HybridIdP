using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Helpers;

/// <summary>
/// Helper class for checking user permissions in Razor Pages
/// </summary>
public static class PermissionHelper
{
    /// <summary>
    /// Get all permissions for the current user
    /// </summary>
    public static async Task<HashSet<string>> GetUserPermissionsAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        System.Security.Claims.ClaimsPrincipal user)
    {
        var permissions = new HashSet<string>();

        // Check if user is admin - admin has all permissions
        if (user.IsInRole(AuthConstants.Roles.Admin))
        {
            return Permissions.GetAll().ToHashSet();
        }

        // Get user ID
        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return permissions;
        }

        // Get user entity
        var appUser = await userManager.FindByIdAsync(userId);
        if (appUser == null)
        {
            return permissions;
        }

        // Get all roles for the user
        var userRoles = await userManager.GetRolesAsync(appUser);

        // Collect permissions from each role
        foreach (var roleName in userRoles)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
            {
                var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim());
                
                foreach (var permission in rolePermissions)
                {
                    permissions.Add(permission);
                }
            }
        }

        return permissions;
    }

    /// <summary>
    /// Check if user has specific permission
    /// </summary>
    public static bool HasPermission(this HashSet<string> permissions, string permission)
    {
        return permissions.Contains(permission);
    }

    /// <summary>
    /// Check if user has any of the specified permissions
    /// </summary>
    public static bool HasAnyPermission(this HashSet<string> permissions, params string[] requiredPermissions)
    {
        return requiredPermissions.Any(p => permissions.Contains(p));
    }
}
