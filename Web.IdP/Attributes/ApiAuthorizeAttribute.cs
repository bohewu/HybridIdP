using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Validation.AspNetCore;

namespace Web.IdP.Attributes;

/// <summary>
/// Specifies that the class or method requires authorization via 
/// either Identity Cookies (for UI) or OpenIddict Bearer Tokens (for API).
/// </summary>
public class ApiAuthorizeAttribute : AuthorizeAttribute
{
    public ApiAuthorizeAttribute()
    {
        // Combine both schemes.
        // This constructor allows us to use static properties that aren't compile-time constants (CS0182 fix).
        AuthenticationSchemes = $"{IdentityConstants.ApplicationScheme},{OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme}";
    }
}
