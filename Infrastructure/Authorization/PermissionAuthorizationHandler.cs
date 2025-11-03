using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler for permission-based access control
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly RoleManager<ApplicationRole> _roleManager;

    public PermissionAuthorizationHandler(RoleManager<ApplicationRole> roleManager)
    {
        _roleManager = roleManager;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Admin role has all permissions
        if (context.User.IsInRole(AuthConstants.Roles.Admin))
        {
            context.Succeed(requirement);
            return;
        }

        // Get user's roles
        var userRoles = context.User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        // Check if any of the user's roles have the required permission
        foreach (var roleName in userRoles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
            {
                var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .ToList();

                if (rolePermissions.Contains(requirement.Permission))
                {
                    context.Succeed(requirement);
                    return;
                }
            }
        }

        // Permission not found
        // Don't call context.Fail() - let other handlers run
    }
}
