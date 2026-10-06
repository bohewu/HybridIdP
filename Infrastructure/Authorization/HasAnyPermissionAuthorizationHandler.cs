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
    private readonly IAdministrativeAuthorizationBoundary _boundary;

    public HasAnyPermissionAuthorizationHandler(RoleManager<ApplicationRole> roleManager, IAdministrativeAuthorizationBoundary boundary)
    {
        _roleManager = roleManager;
        _boundary = boundary;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        HasAnyPermissionRequirement requirement)
    {
        var authority = await _boundary.ResolveAsync();
        if (authority == null || authority.Principal.Identity?.IsAuthenticated != true) return;
        if (authority.IsBearer)
        {
            if (requirement.Permissions.Any(authority.Permissions.Contains)) context.Succeed(requirement);
            return;
        }
        var principal = authority.Principal;
        // 1) Direct permission claims for cookie-authenticated interactive users.
        var permissionClaims = principal.FindAll("permission")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requirement.Permissions.Any(permissionClaims.Contains))
        {
            context.Succeed(requirement);
            return;
        }

        // 2) Prefer active role when present.
        var activeRoleName = principal.Claims.FirstOrDefault(c => c.Type == "active_role")?.Value;
        if (!string.IsNullOrWhiteSpace(activeRoleName))
        {
            if (await RoleHasAnyPermissionAsync(activeRoleName, requirement.Permissions))
            {
                context.Succeed(requirement);
            }
            return;
        }

        // 3) Fallback for sessions without active_role: evaluate IdP roles only.
        var roleNames = AuthorizationRoleClaimResolver.GetIdpRoleNames(principal);

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
