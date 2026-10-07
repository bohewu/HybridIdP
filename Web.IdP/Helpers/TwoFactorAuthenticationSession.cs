using System.Security.Claims;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Helpers;

public static class TwoFactorAuthenticationSession
{
    public static ClaimsIdentity CreateIdentity(ApplicationUser user, UserManager<ApplicationUser> users)
    {
        if (string.IsNullOrEmpty(user.SecurityStamp))
            throw new InvalidOperationException("A pending sign-in requires a security stamp.");
        return new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(users.Options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp)
        ], IdentityConstants.TwoFactorUserIdScheme);
    }

    public static async Task<ApplicationUser?> GetUserAsync(HttpContext http, UserManager<ApplicationUser> users)
    {
        var authentication = await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var principal = authentication.Principal;
        if (!authentication.Succeeded || principal?.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        var user = await users.FindByIdAsync(id.ToString());
        return user != null && !string.IsNullOrEmpty(user.SecurityStamp) &&
            principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType) == user.SecurityStamp
            ? user : null;
    }
}
