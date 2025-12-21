using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Attributes;

/// <summary>
/// Specifies that the class or method requires authorization via 
/// either full Identity Cookies or TwoFactorUserId (partial auth during MFA enrollment).
/// This allows users to set up MFA before completing their full login.
/// </summary>
public class MfaSetupAuthorizeAttribute : AuthorizeAttribute
{
    public MfaSetupAuthorizeAttribute()
    {
        // Combine both schemes: full auth + partial MFA setup auth.
        // This constructor allows us to use static properties that aren't compile-time constants (CS0182 fix).
        AuthenticationSchemes = $"{IdentityConstants.ApplicationScheme},{IdentityConstants.TwoFactorUserIdScheme}";
    }
}
