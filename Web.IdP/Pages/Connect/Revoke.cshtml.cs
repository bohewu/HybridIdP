using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Pages.Connect;

public class RevokeModel : PageModel
{
    private readonly IOpenIddictTokenManager _tokenManager;

    public RevokeModel(IOpenIddictTokenManager tokenManager)
    {
        _tokenManager = tokenManager;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Retrieve the token from the database using the token hint
        var token = await _tokenManager.FindByIdAsync(request.Token ?? string.Empty);
        if (token != null)
        {
            // Revoke the token (mark as revoked in database)
            await _tokenManager.TryRevokeAsync(token);
        }

        // Return success (200 OK) regardless of whether token was found
        // This prevents token scanning attacks
        return Page();
    }
}
