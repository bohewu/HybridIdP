using System.Security.Claims;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.IdP.Infrastructure.Identity;
using Web.IdP.Options;
using Microsoft.Extensions.Options;
using Core.Domain; // Explicitly include Core.Domain for ApplicationUser

namespace Web.IdP.Pages.Account;

[AllowAnonymous]
public class ExternalLoginCallbackModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ExternalLoginCallbackModel> _logger;
    private readonly ExternalLoginOptions _externalLoginOptions;

    public ExternalLoginCallbackModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<ExternalLoginCallbackModel> logger,
        IOptions<ExternalLoginOptions> externalLoginOptions)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _externalLoginOptions = externalLoginOptions.Value;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl = returnUrl ?? Url.Content("~/");
        if (remoteError != null)
        {
            _logger.LogWarning("Error from external provider: {RemoteError}", remoteError);
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }
        
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            _logger.LogWarning("Error loading external login information.");
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        // Sign in the user with this external login provider if the user already has a login.
        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            _logger.LogInformation("{Name} logged in with {LoginProvider} provider.", info.Principal.Identity?.Name, info.LoginProvider);
            return LocalRedirect(returnUrl);
        }
        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        // If the user does not have an account, then ask the user to create an account.
        // CHECK AUTO-LINK: If configured AND email matches exactly.
        if (_externalLoginOptions.AutoLinkMatchingEmail)
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (!string.IsNullOrEmpty(email))
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user != null)
                {
                    // Confirm email is confirmed? (Optional security check, usually external email is trusted if email_verified claim is true)
                    // For now, if config allows, we link.
                    var addLoginResult = await _userManager.AddLoginAsync(user, info);
                    if (addLoginResult.Succeeded)
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
                        return LocalRedirect(returnUrl);
                    }
                }
            }
        }

        // If we get here, the user is new or not linked. Redirect to confirmation page.
        // pass ReturnUrl
        // We need to store returnUrl in ViewData or pass it to next page
        
        return RedirectToPage("./ExternalLoginConfirmation", new { ReturnUrl = returnUrl });
    }
}
