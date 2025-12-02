using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler for permission-based access control
/// Phase 11.4: Modified to check only ActiveRoleId permissions (no role aggregation)
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
        // Phase 11.4: Get the active role from claims
        // The active role is set during login/role selection and stored in the session
        var activeRoleClaim = context.User.Claims
            .FirstOrDefault(c => c.Type == "active_role");

        string? activeRoleName = null;

        if (activeRoleClaim != null)
        {
            // Active role is explicitly set in claims (preferred for Phase 11)
            activeRoleName = activeRoleClaim.Value;
        }
        else
        {
            // Fallback: If no active_role claim, use the first role claim
            // This maintains backward compatibility for sessions created before Phase 11
            activeRoleName = context.User.Claims
                .FirstOrDefault(c => c.Type == ClaimTypes.Role)
                ?.Value;
        }

        if (string.IsNullOrEmpty(activeRoleName))
        {
            // No active role found - deny access
            return;
        }

        // Admin role has all permissions
        if (activeRoleName == AuthConstants.Roles.Admin)
        {
            context.Succeed(requirement);
            return;
        }

        // Check if the active role has the required permission
        var role = await _roleManager.FindByNameAsync(activeRoleName);
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

        // Permission not found in active role
        // Don't call context.Fail() - let other handlers run
    }
}
