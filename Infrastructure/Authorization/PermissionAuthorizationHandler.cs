using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler for permission-based access control
/// Prefers active-role permissions when available, with safe fallback for sessions
/// that don't carry an active_role claim.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IAdministrativeAuthorizationBoundary _boundary;

    public PermissionAuthorizationHandler(RoleManager<ApplicationRole> roleManager, IAdministrativeAuthorizationBoundary boundary)
    {
        _roleManager = roleManager;
        _boundary = boundary;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var authority = await _boundary.ResolveAsync();
        if (authority == null || authority.Principal.Identity?.IsAuthenticated != true) return;
        if (authority.IsBearer)
        {
            if (authority.Permissions.Contains(requirement.Permission)) context.Succeed(requirement);
            return;
        }
        var principal = authority.Principal;
        // 3) Direct permission claims for cookie-authenticated interactive users.
        var permissionClaims = principal.FindAll("permission");
        if (permissionClaims.Any(c => string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
            return;
        }

        // 4) Prefer active role when present.
        var activeRoleName = principal.Claims.FirstOrDefault(c => c.Type == "active_role")?.Value;
        if (!string.IsNullOrWhiteSpace(activeRoleName))
        {
            if (await RoleHasPermissionAsync(activeRoleName, requirement.Permission))
            {
                context.Succeed(requirement);
            }
            return;
        }

        // 5) Fallback for sessions without active_role: evaluate IdP roles only.
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
