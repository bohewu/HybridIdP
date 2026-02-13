using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler for permission-based access control
/// Prefers active-role permissions when available, with safe fallback for sessions
/// that don't carry an active_role claim.
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
        // 1) Scopes for M2M/client-credentials principals.
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

        // 2) "scp" claims (Azure AD style).
        var scpClaims = context.User.FindAll("scp");
        if (scpClaims.Any(c => string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase)))
        {
             context.Succeed(requirement);
             return;
        }

        // 3) Direct permission claims for cookie-authenticated interactive users.
        var permissionClaims = context.User.FindAll("permission");
        if (permissionClaims.Any(c => string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
            return;
        }

        // 4) Prefer active role when present.
        var activeRoleName = context.User.Claims.FirstOrDefault(c => c.Type == "active_role")?.Value;
        if (!string.IsNullOrWhiteSpace(activeRoleName))
        {
            if (await RoleHasPermissionAsync(activeRoleName, requirement.Permission))
            {
                context.Succeed(requirement);
            }
            return;
        }

        // 5) Fallback for sessions without active_role: evaluate all role claims.
        var roleNames = context.User.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roleNames.Count == 0)
        {
            return;
        }

        if (roleNames.Any(r => string.Equals(r, AuthConstants.Roles.Admin, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
            return;
        }

        foreach (var roleName in roleNames)
        {
            if (await RoleHasPermissionAsync(roleName, requirement.Permission))
            {
                context.Succeed(requirement);
                return;
            }
        }

        // Don't call context.Fail() - let other handlers run.
    }

    private async Task<bool> RoleHasPermissionAsync(string roleName, string permission)
    {
        if (string.Equals(roleName, AuthConstants.Roles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var role = await _roleManager.FindByNameAsync(roleName);
        if (role == null || string.IsNullOrWhiteSpace(role.Permissions))
        {
            return false;
        }

        return role.Permissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
    }
}
