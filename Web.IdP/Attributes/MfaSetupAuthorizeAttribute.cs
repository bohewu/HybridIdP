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
        // Only TwoFactorUserIdScheme - for users in the middle of MFA enrollment.
        // Fully authenticated users should use the regular /api/account/mfa/* endpoints.
        AuthenticationSchemes = IdentityConstants.TwoFactorUserIdScheme;
    }
}
