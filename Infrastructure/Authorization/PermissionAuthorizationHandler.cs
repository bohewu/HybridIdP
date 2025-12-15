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
        // Check for Scopes (M2M / Client Credentials)
        // Scopes are typically used for machine-to-machine communication where there is no user/role
        
        // 1. Check "scope" claim (space-separated, standard OAuth2)
        var scopeClaim = context.User.FindFirst("scope");
        if (scopeClaim != null && !string.IsNullOrWhiteSpace(scopeClaim.Value))
        {
            var scopes = scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (scopes.Contains(requirement.Permission, StringComparer.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
                return;
            }
        }

        // 2. Check "scp" claim (repeated claim, Azure AD style)
        var scpClaims = context.User.FindAll("scp");
        if (scpClaims.Any(c => string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase)))
        {
             context.Succeed(requirement);
             return;
        }

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
            // Fallback: If no active_role claim, try ClaimTypes.Role (URI) or "role" (JWT)
            activeRoleName = context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value
                             ?? context.User.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
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
