using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Pages.Connect;

// [Authorize] // Remove Authorize here, manage it inside to allow handling the flow
public class DeviceModel : PageModel
{
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;

    public DeviceModel(
        IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager)
    {
        _scopeManager = scopeManager;
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
    }

    [BindProperty(Name = "user_code")]
    public string? UserCode { get; set; }

    public IActionResult OnGet()
    {
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(UserCode))
        {
            ModelState.AddModelError(nameof(UserCode), "The user code is required.");
            return Page();
        }

        // Retrieve the OpenIddict request from the context
        // This requires EnableEndUserVerificationEndpointPassthrough() in Program.cs
        var request = HttpContext.GetOpenIddictServerRequest();

        // If request is null, it means the middleware didn't locate/validate the user_code request 
        // (possibly invalid code or param name mismatch if not handled by bind).
        // Actually, if passthrough is on, the request should be available if we conform to protocol.
        // But for "User Code" input form, we are posting "user_code".

        // Note: OpenIddict might NOT automatically validate the code on Passthrough unless we ask it to?
        // But let's assume standard flow.

        // Authenticate the user (IdP session)
        var authenticateResult = await HttpContext.AuthenticateAsync(); // Default scheme (Cookies)
        if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
        {
            // If not logged in, challenge to login, then return here?
            // Simple approach: Challenge.
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = "/connect/verify"
            });
        }

        var user = authenticateResult.Principal;
        // Create the claims principal for the *Device* (the "user" approving it)
        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            Claims.Name,
            Claims.Role);

        identity.AddClaim(Claims.Subject, user.GetClaim(Claims.Subject)!);
        identity.AddClaim(Claims.Name, user.GetClaim(Claims.Name)!);

        var principal = new ClaimsPrincipal(identity);

        if (request != null)
        {
            principal.SetScopes(request.GetScopes());
        }

        principal.SetResources(await _scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());

        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = null,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = null
        });

        return SignIn(principal, properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
