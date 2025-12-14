using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Controllers.Connect;

/// <summary>
/// Handles OIDC RP-Initiated Logout (end_session endpoint).
/// For direct IdP admin UI logout, use /Account/Logout instead.
/// </summary>
public class LogoutController : Controller
{
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request == null)
        {
            return BadRequest("The OpenID Connect request cannot be retrieved.");
        }

        // GET request: show confirmation page if user is authenticated
        if (HttpMethods.IsGet(Request.Method))
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                // Not authenticated, just complete the logout flow
                return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            // Show confirmation view
            return View("~/Views/Connect/Logout.cshtml");
        }

        // POST request: perform the sign-out
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        
        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
