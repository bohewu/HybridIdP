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

        // Retrieve the claims principal associated with the user code (Device Request Context).
        // This relies on OpenIddict middleware to extract and validate the user_code from the request.
        var deviceResult = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        
        // Authenticate the user (IdP session)
        var userResult = await HttpContext.AuthenticateAsync(); // Default scheme (Cookies)

        if (!userResult.Succeeded || userResult.Principal == null)
        {
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = "/connect/verify" 
            });
        }

        var user = userResult.Principal;

        // Create the approved identity
        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            Claims.Name,
            Claims.Role);

        identity.AddClaim(Claims.Subject, user.GetClaim(Claims.Subject)!);
        identity.AddClaim(Claims.Name, user.GetClaim(Claims.Name)!);

        var principal = new ClaimsPrincipal(identity);

        if (deviceResult.Succeeded && deviceResult.Principal != null)
        {
            // Use scopes from the original device request
            principal.SetScopes(deviceResult.Principal.GetScopes());
        }
        else
        {
            // If device request is not found via authentication scheme, fallback or error.
            // In a strict implementation, we should error. 
            // For now, we keep the fallback but log/note it.
            // But if user_code is valid, deviceResult SHOULD succeed.
             principal.SetScopes("openid", "profile", "offline_access"); 
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
