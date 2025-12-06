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

[Authorize]
public class DeviceModel : PageModel
{
    private readonly IOpenIddictScopeManager _scopeManager;

    public DeviceModel(IOpenIddictScopeManager scopeManager)
    {
        _scopeManager = scopeManager;
    }

    [BindProperty]
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

        // Retrieve the device authorization request from the user code
        // Note: In a real implementation, you might want to normalize the code (e.g. uppercase, add hyphens if missing)
        // For now, we assume the user enters it correctly or we could add simple normalization.

        var request = HttpContext.GetOpenIddictServerRequest();
        // Wait, for verification endpoint, the request is NOT automatically extracted from the query on POST
        // unless we are using the OpenIddict middleware to handle the initial GET and pass parameters.
        // Actually, standard OpenIddict flow for verification:
        // 1. User goes to /connect/verify (possibly with ?user_code=...)
        // 2. We extract code.
        // 3. We call _interactionService (in IdentityServer) or equivalent in OpenIddict context.

        // In OpenIddict, we don't have a high-level "interaction service" for device flow built-in for the UI controller 
        // in the same way. We need to manually validate the user code if we want to show consent.

        // However, the simplest implementation for OpenIddict passthrough:
        // The POST /connect/verify action should eventually return a SignInResult (Accept) or Forbid (Deny).
        // But first we need to identify the request context involved with this UserCode.

        // Actually, the OpenIddict Server handler will handle the "verification" validation if we pass it back?
        // No, typically we need to:
        // 1. Validate the user code matches a pending device authorization.
        // 2. Ask user for consent (Implicit or explicit).
        // 3. Mark the authorization as complete.

        // For simplicity in Phase 13.3, let's implement the "Submit" -> "Consent" flow or just "Submit" -> "Approved".
        // To approve, we need to sign in with the user's principal.

        // But how do we link the UserCode to the pending device authorization?
        // We probably need to "extract" the request using the User Code.
        // OpenIddict doesn't automatically bind the request based on UserCode in the POST body for us in the PageModel
        // UNLESS we forward the request to the OpenIddict middleware or use the Application Manager.

        // Let's look at OpenIddict samples.
        // Usually, we redirect to a consent page with the user_code.
        // But here, we ARE the verification page.

        // We will try to just set the "user_code" in the properties and sign in?
        // No, standard flow:
        // The user submits the code. We validate it.

        // To validate, we can use IOpenIddictTokenManager or ApplicationManager? No.
        // We can use the OpenIddict server events?

        // Actually, simpler approach:
        // The passthrough allows us to handle the request.
        // If we want OpenIddict to validate the code, we might need to invoke it.

        // Let's assume the user calls this page.
        // On POST:
        var principal = (await HttpContext.AuthenticateAsync()).Principal;
        if (principal == null)
        {
            return Challenge(); // Force login
        }

        // Construct the ClaimsPrincipal to return
        // We need to know who the client is and what scopes were requested to form the principal properly.
        // BUT we don't know the client just from the code unless we look it up.

        // Does OpenIddict provide a way to retrieve the device authorization by user code?
        // Yes, IOpenIddictAuthorizationManager or similar?
        // Actually, usually it's mostly internal.

        // STARTUP FIX: 
        // If we use the provided input `UserCode` and pass it to OpenIddict?
        // Actually, the standard pattern in OpenIddict passthrough for Verification:
        // GET /connect/verify -> Show form.
        // POST /connect/verify -> 
        //   request = HttpContext.GetOpenIddictServerRequest() is NULL because it's looking for standard parameters?
        //   Or maybe valid if user_code is in query?

        // Let's assume we just want to approve.
        // We can return a specific SignInResult.

        // For this first iteration, let's use a simplified approach:
        // Use the `user_code` parameter to context.

        return Page(); // Placeholder, I need to implement the actual logic in next step.
    }
}
