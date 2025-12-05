using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Pages.Connect;

public class IntrospectModel : PageModel
{
    private readonly IOpenIddictTokenManager _tokenManager;

    public IntrospectModel(IOpenIddictTokenManager tokenManager)
    {
        _tokenManager = tokenManager;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Retrieve the token from the database using the token hint
        var token = await _tokenManager.FindByIdAsync(request.Token ?? string.Empty);
        if (token == null)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new Microsoft.AspNetCore.Authentication.AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The specified token is invalid."
                }));
        }

        // Return the token introspection response
        // OpenIddict will automatically handle the response format
        return new SignInResult(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            new System.Security.Claims.ClaimsPrincipal(),
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties());
    }
}
