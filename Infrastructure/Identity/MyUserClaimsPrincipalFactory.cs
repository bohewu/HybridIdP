using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Infrastructure.Identity;

public class MyUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly IApplicationDbContext _context;

    public MyUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        IApplicationDbContext context) : base(userManager, roleManager, optionsAccessor)
    {
        _context = context;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        // Phase 10.4: Load Person navigation property if not already loaded
        if (user.PersonId.HasValue && user.Person == null)
        {
            user.Person = await _context.Persons.FindAsync(user.PersonId.Value);
        }

        var identity = await base.GenerateClaimsAsync(user);

        // Ensure preferred_username claim for downstream clients
        var preferredUsername = user.Email ?? user.UserName ?? string.Empty;
        if (!string.IsNullOrEmpty(preferredUsername) && !identity.HasClaim(c => c.Type == AuthConstants.Claims.PreferredUsername))
        {
            identity.AddClaim(new Claim(AuthConstants.Claims.PreferredUsername, preferredUsername));
        }

        // Phase 10.4: Add profile claims from Person (with fallback to ApplicationUser)
        var department = user.Person?.Department ?? user.Department;
        if (!string.IsNullOrEmpty(department))
        {
            identity.AddClaim(new Claim(AuthConstants.Claims.Department, department));
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
