using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler that checks if user has ANY of the specified permissions
/// Phase 11.4: Uses active role for permission check
/// </summary>
public class HasAnyPermissionAuthorizationHandler : AuthorizationHandler<HasAnyPermissionRequirement>
{
    private readonly RoleManager<ApplicationRole> _roleManager;

    public HasAnyPermissionAuthorizationHandler(RoleManager<ApplicationRole> roleManager)
    {
        _roleManager = roleManager;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        HasAnyPermissionRequirement requirement)
    {
        // Phase 11.4: Get the active role from claims
        var activeRoleClaim = context.User.Claims
            .FirstOrDefault(c => c.Type == "active_role");

        string? activeRoleName = null;

        if (activeRoleClaim != null)
        {
            activeRoleName = activeRoleClaim.Value;
        }
        else
        {
            // Fallback: If no active_role claim, use the first role claim
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

        // Check if the active role has ANY of the required permissions
        var role = await _roleManager.FindByNameAsync(activeRoleName);
        if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
        {
            var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToHashSet();

            // Check if any required permission exists in role permissions
            if (requirement.Permissions.Any(p => rolePermissions.Contains(p)))
            {
                context.Succeed(requirement);
                return;
            }
        }

        // No matching permission found in active role
        // Don't call context.Fail() - let other handlers run
    }
}
