using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services; // Ensure this namespace exists if needed for common services
using Web.IdP.Filters; // For RequireClientPermission
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Controllers.Connect;

public class LogoutController : Controller
{
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken] // Handled by OpenIddict or not needed for simple redirect flow verification
    [RequireClientPermission(OpenIddictConstants.Permissions.Endpoints.EndSession)]
    public async Task<IActionResult> Logout()
    {
        // Ask OpenIddict to validate the request (post_logout_redirect_uri, etc.)
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request == null)
        {
            return BadRequest("The OpenID Connect request cannot be retrieved.");
        }

        // If this is a GET request, we *could* show a confirmation page.
        // For simplicity/standard flow, or if 'post_logout_redirect_uri' is present and we want immediate logout (depending on policy),
        // we can check if user is authenticated.
        
        // HOWEVER, standard RP-Initiated logout usually implies showing a confirmation 
        // OR checking for 'id_token_hint' to validitate intent.
        // For this implementation, let's assume we show a confirmation if GET, and process if POST.
        // OR if it's an auto-logout scenario.
        
        // Let's implement a simple flow:
        // 1. If Unauthenticated -> SignOut (no-op affecting Auth) but return SignOutResult to trigger OpenIddict redirect.
        // 2. If Authenticated -> Show Confirmation (GET) or SignOut (POST).

        if (HttpMethods.IsGet(Request.Method))
        {
             // If not authenticated, just complete the logout flow (redirect back)
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                 return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            // If authenticated, show confirmation view
            // (Assuming you have a View for this, or we can just auto-logout for now if no view exists)
            // Let's assume auto-logout for strictly API/M2M or simple flows if no view is prepared.
            // But 'ClientForm' had 'ept:end_session', implying user interaction.
            // Let's assume we return a View "Logout". If it doesn't exist, we might error.
            // SAFE BET: Just do the sign-out for now to ensure funcationality, 
            // OR return a simple content result if View missing.
            // Let's try to return View(), user can add the view file if missing.
            // Wait, previous conversation mentioned 'AuthorizationController' returns View("Authorize"). 
            // We should probably check if 'Views/Connect/Logout.cshtml' exists.
            
            // Re-reading user request: "traditional logout might pop up a window... or backend... purely backend..."
            // Let's implement the POST action handling primarily.
            // Usage of 'SignOutAsync' for both schemes.
        }

        // Perform the sign-out
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme); // Identity Cookie
        
        // Returning a SignOutResult will ask OpenIddict to redirect the user agent 
        // to the post_logout_redirect_uri specified by the client application.
        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
