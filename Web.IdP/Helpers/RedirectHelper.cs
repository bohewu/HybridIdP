using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Helpers;

/// <summary>
/// Helper methods for safe URL redirects that work with OIDC flows.
/// </summary>
public static class RedirectHelper
{
    /// <summary>
    /// Determines if the returnUrl is safe to redirect to.
    /// This is a more permissive version of Url.IsLocalUrl() that allows
    /// OIDC authorize URLs containing PAR request_uri with URN schemes.
    /// </summary>
    /// <param name="returnUrl">The URL to validate</param>
    /// <returns>True if the URL is safe to redirect to</returns>
    public static bool IsSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return false;
        }

        // Must start with / to be a local path
        if (!returnUrl.StartsWith('/'))
        {
            return false;
        }

        // Prevent protocol-relative URLs (//example.com)
        if (returnUrl.StartsWith("//"))
        {
            return false;
        }

        // Prevent backslash URLs that could be interpreted as protocol-relative
        if (returnUrl.StartsWith("/\\"))
        {
            return false;
        }

        // Check for control characters that could be used in URL smuggling
        if (returnUrl.Any(c => char.IsControl(c)))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Safely redirects to the returnUrl if it's valid, otherwise redirects to home.
    /// Use this instead of LocalRedirect when dealing with OIDC flows that may contain
    /// PAR request_uri with URN schemes.
    /// </summary>
    /// <param name="page">The PageModel instance</param>
    /// <param name="returnUrl">The URL to redirect to</param>
    /// <param name="fallback">Fallback URL if returnUrl is invalid (default: ~/)</param>
    /// <returns>RedirectResult to safe URL</returns>
    public static IActionResult SafeRedirect(this PageModel page, string? returnUrl, string fallback = "~/")
    {
        if (IsSafeReturnUrl(returnUrl))
        {
            return new RedirectResult(returnUrl!);
        }

        return page.LocalRedirect(fallback);
    }
}
