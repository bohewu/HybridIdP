using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Services;

public class RevocationService : IRevocationService
{
    private readonly IOpenIddictTokenManager _tokenManager;

    public RevocationService(IOpenIddictTokenManager tokenManager)
    {
        _tokenManager = tokenManager;
    }

    public async Task<IActionResult> HandleRevocationRequestAsync(OpenIddictRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Retrieve the token from the database using the token hint
        var token = await _tokenManager.FindByIdAsync(request.Token ?? string.Empty);
        if (token != null)
        {
            // Revoke the token (mark as revoked in database)
            await _tokenManager.TryRevokeAsync(token);
        }

        // Return success (200 OK) regardless of whether token was found
        // This prevents token scanning attacks
        return new OkResult();
    }
}
