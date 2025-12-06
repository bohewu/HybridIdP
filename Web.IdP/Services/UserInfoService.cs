using System.Security.Claims;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Services;

public class UserInfoService : IUserInfoService
{
    public Task<Dictionary<string, object>> GetUserInfoAsync(ClaimsPrincipal principal)
    {
        if (principal == null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        var userinfo = new Dictionary<string, object>
        {
            [Claims.Subject] = principal.GetClaim(Claims.Subject) ?? "",
        };

        // Add username if available
        var username = principal.GetClaim(Claims.Username) ?? principal.GetClaim(Claims.PreferredUsername);
        if (!string.IsNullOrEmpty(username))
        {
            userinfo[Claims.PreferredUsername] = username;
        }

        // Add email if available
        var email = principal.GetClaim(Claims.Email);
        if (!string.IsNullOrEmpty(email))
        {
            userinfo[Claims.Email] = email;
            
            var emailVerified = principal.GetClaim(Claims.EmailVerified);
            if (!string.IsNullOrEmpty(emailVerified))
            {
                userinfo[Claims.EmailVerified] = emailVerified == "true";
            }
        }

        // Add name if available
        var name = principal.GetClaim(Claims.Name);
        if (!string.IsNullOrEmpty(name))
        {
            userinfo[Claims.Name] = name;
        }

        // Add given name if available
        var givenName = principal.GetClaim(Claims.GivenName);
        if (!string.IsNullOrEmpty(givenName))
        {
            userinfo[Claims.GivenName] = givenName;
        }

        // Add family name if available
        var familyName = principal.GetClaim(Claims.FamilyName);
        if (!string.IsNullOrEmpty(familyName))
        {
            userinfo[Claims.FamilyName] = familyName;
        }

        // Add roles if available
        var roles = principal.GetClaims(Claims.Role);
        if (roles.Any())
        {
            userinfo[Claims.Role] = roles;
        }

        return Task.FromResult(userinfo);
    }
}
