using System.Security.Claims;

namespace Infrastructure.Authorization;

public static class AuthorizationRoleClaimResolver
{
    public static IReadOnlyList<string> GetIdpRoleNames(ClaimsPrincipal principal)
    {
        var appRoleCounts = principal.FindAll("app_role")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

        var idpRoleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in principal.Claims
                     .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                     .Select(c => c.Value)
                     .Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (appRoleCounts.TryGetValue(roleName, out var appRoleCount) && appRoleCount > 0)
            {
                appRoleCounts[roleName] = appRoleCount - 1;
                continue;
            }

            idpRoleNames.Add(roleName);
        }

        return idpRoleNames.ToList();
    }

    public static bool IsInIdpRole(ClaimsPrincipal principal, string roleName)
    {
        return GetIdpRoleNames(principal)
            .Contains(roleName, StringComparer.OrdinalIgnoreCase);
    }
}
