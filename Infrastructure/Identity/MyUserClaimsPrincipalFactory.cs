using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Infrastructure.Identity;

public class MyUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    public MyUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
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

        return identity;
    }
}
