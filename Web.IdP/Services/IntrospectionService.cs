using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Services;

public class IntrospectionService : IIntrospectionService
{
    private readonly IOpenIddictTokenManager _tokenManager;

    public IntrospectionService(IOpenIddictTokenManager tokenManager)
    {
        _tokenManager = tokenManager;
    }

    public async Task<IActionResult> HandleIntrospectionRequestAsync(OpenIddictRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Retrieve the token from the database using the token hint
        var token = await _tokenManager.FindByIdAsync(request.Token ?? string.Empty);
        if (token == null)
        {
            return new ForbidResult(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                new AuthenticationProperties(new Dictionary<string, string?>
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
            new AuthenticationProperties());
    }
}
