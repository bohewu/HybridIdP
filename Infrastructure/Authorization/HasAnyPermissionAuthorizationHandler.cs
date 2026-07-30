using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler that checks if user has ANY of the specified permissions
/// Prefers active-role permissions when available, with safe fallback for sessions
/// that don't carry an active_role claim.
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
        // 1) Direct permission claims for cookie-authenticated interactive users.
        var permissionClaims = context.User.FindAll("permission")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requirement.Permissions.Any(permissionClaims.Contains))
        {
            context.Succeed(requirement);
            return;
        }

        // 2) Prefer active role when present.
        var activeRoleName = context.User.Claims.FirstOrDefault(c => c.Type == "active_role")?.Value;
        if (!string.IsNullOrWhiteSpace(activeRoleName))
        {
            if (await RoleHasAnyPermissionAsync(activeRoleName, requirement.Permissions))
            {
                context.Succeed(requirement);
            }
            return;
        }

        // 3) Fallback for sessions without active_role: evaluate IdP roles only.
        var roleNames = AuthorizationRoleClaimResolver.GetIdpRoleNames(context.User);

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
            if (await RoleHasAnyPermissionAsync(roleName, requirement.Permissions))
            {
                context.Succeed(requirement);
                return;
            }
        }

        // Don't call context.Fail() - let other handlers run.
    }

    private async Task<bool> RoleHasAnyPermissionAsync(string roleName, IEnumerable<string> requiredPermissions)
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

        var rolePermissions = role.Permissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requiredPermissions.Any(rolePermissions.Contains);
    }
}
