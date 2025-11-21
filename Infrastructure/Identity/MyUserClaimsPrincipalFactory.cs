using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Infrastructure.Identity;

public class MyUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public MyUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor) : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // Ensure preferred_username claim for downstream clients
        var preferredUsername = user.Email ?? user.UserName ?? string.Empty;
        if (!string.IsNullOrEmpty(preferredUsername) && !identity.HasClaim(c => c.Type == AuthConstants.Claims.PreferredUsername))
        {
            identity.AddClaim(new Claim(AuthConstants.Claims.PreferredUsername, preferredUsername));
        }

        // Add permission claims from user's roles
        var userRoles = await UserManager.GetRolesAsync(user);
        var permissions = new HashSet<string>();

        foreach (var roleName in userRoles)
        {
            var role = await RoleManager.FindByNameAsync(roleName);
            if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
            {
                // Parse permissions from the role's Permissions property (comma-separated string)
                var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p));
                
                foreach (var permission in rolePermissions)
                {
                    permissions.Add(permission);
                }
            }
        }

        // Add permission claims to identity
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim("permission", permission));
        }

        return identity;
    }
}
