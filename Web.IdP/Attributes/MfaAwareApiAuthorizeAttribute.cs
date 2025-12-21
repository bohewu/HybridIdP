using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Validation.AspNetCore;

namespace Web.IdP.Attributes;

/// <summary>
/// Specifies that the class or method requires authorization via:
/// 1. Identity Cookies (Application scheme) - for normal logged-in users
/// 2. OpenIddict Bearer Tokens - for API clients
/// 3. TwoFactorUserId scheme - for users in the MFA setup flow (partial auth)
/// </summary>
public class MfaAwareApiAuthorizeAttribute : AuthorizeAttribute
{
    public MfaAwareApiAuthorizeAttribute()
    {
        AuthenticationSchemes = $"{IdentityConstants.ApplicationScheme},{OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme},{IdentityConstants.TwoFactorUserIdScheme}";
    }
}
