using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;

namespace Web.IdP.Controllers.Connect;

/// <summary>
/// Handles OIDC RP-Initiated Logout (end_session endpoint).
/// For direct IdP admin UI logout, use /Account/Logout instead.
/// </summary>
public class LogoutController : Controller
{
    private readonly IAntiforgery _antiforgery;

    public LogoutController(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

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

        if (User.Identity?.IsAuthenticated != true)
        {
            // No local session to clear; let OpenIddict complete the validated request.
            return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // RP GET/POST ingress has no local antiforgery token. Only the local form
        // can confirm sign-out, with a token bound to this browser and user.
        if (!HttpMethods.IsPost(Request.Method) || !Request.HasFormContentType ||
            !Request.Form.ContainsKey("logout_confirmation"))
        {
            return View("~/Views/Connect/Logout.cshtml", request);
        }

        if (Request.Form["logout_confirmation"].ToString() != "true")
        {
            return BadRequest();
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest();
        }

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        
        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
