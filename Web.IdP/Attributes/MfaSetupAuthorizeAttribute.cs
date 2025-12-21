using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Attributes;

/// <summary>
/// Specifies that the class or method requires authorization via 
/// TwoFactorUserId scheme only (partial auth during mandatory MFA enrollment).
/// This is used for MFA setup endpoints where users have verified their password
/// but haven't completed full authentication yet.
/// </summary>
public class MfaSetupAuthorizeAttribute : AuthorizeAttribute
{
    public MfaSetupAuthorizeAttribute()
    {
        // Support both TwoFactorUserIdScheme (during login) and standard ApplicationScheme (step-up after login).
        AuthenticationSchemes = $"{IdentityConstants.ApplicationScheme},{IdentityConstants.TwoFactorUserIdScheme}";
    }
}
