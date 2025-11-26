using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Api;

/// <summary>
/// OpenID Connect UserInfo endpoint
/// Returns user claims based on the granted scopes
/// Requires 'openid' scope per OIDC specification
/// </summary>
[ApiController]
public class UserinfoController : ControllerBase
{
    [Authorize(
        AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
        Policy = "RequireScope:openid")]
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    public IActionResult Userinfo()
    {
        var claims = User.Claims.ToList();
        
        // Build the userinfo response
        var userinfo = new Dictionary<string, object>
        {
            [Claims.Subject] = User.GetClaim(Claims.Subject) ?? "",
        };

        // Add username if available
        var username = User.GetClaim(Claims.Username) ?? User.GetClaim(Claims.PreferredUsername);
        if (!string.IsNullOrEmpty(username))
        {
            userinfo[Claims.PreferredUsername] = username;
        }

        // Add email if available
        var email = User.GetClaim(Claims.Email);
        if (!string.IsNullOrEmpty(email))
        {
            userinfo[Claims.Email] = email;
            
            var emailVerified = User.GetClaim(Claims.EmailVerified);
            if (!string.IsNullOrEmpty(emailVerified))
            {
                userinfo[Claims.EmailVerified] = emailVerified == "true";
            }
        }

        // Add name if available
        var name = User.GetClaim(Claims.Name);
        if (!string.IsNullOrEmpty(name))
        {
            userinfo[Claims.Name] = name;
        }

        // Add given name if available
        var givenName = User.GetClaim(Claims.GivenName);
        if (!string.IsNullOrEmpty(givenName))
        {
            userinfo[Claims.GivenName] = givenName;
        }

        // Add family name if available
        var familyName = User.GetClaim(Claims.FamilyName);
        if (!string.IsNullOrEmpty(familyName))
        {
            userinfo[Claims.FamilyName] = familyName;
        }

        // Add roles if available
        var roles = User.GetClaims(Claims.Role);
        if (roles.Any())
        {
            userinfo[Claims.Role] = roles;
        }

        return Ok(userinfo);
    }
}
