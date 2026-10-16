using Core.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.IdP.Helpers;
using Web.IdP.Attributes;

namespace Web.IdP.Pages.Account;

[LogoutAntiforgeryRecovery]
public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(
        SignInManager<ApplicationUser> signInManager,
        ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    public bool ShowLogoutPrompt { get; set; } = true;
    public bool ConfirmationExpired { get; private set; }
    public string CurrentAccountName => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public IActionResult OnGet(bool confirmationExpired = false)
    {
        ConfirmationExpired = confirmationExpired;
        ShowLogoutPrompt = User.Identity?.IsAuthenticated == true;
        if (!ShowLogoutPrompt && !ConfirmationExpired)
        {
            // User is not authenticated, redirect to home
            return RedirectToPage("/Index");
        }

        // Show logout confirmation
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out.");

        ShowLogoutPrompt = false;

        if (returnUrl != null)
        {
            return this.SafeRedirect(returnUrl);
        }
        else
        {
            // This needs to be a redirect so that the browser performs a new
            // request and the identity for the user gets updated.
            return RedirectToPage();
        }
    }
}
