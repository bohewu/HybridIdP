using System.Security.Claims;
using System.Text.Json;
using Core.Domain.Entities;
using Core.Domain.Constants;
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
public partial class ExternalLoginCallbackModel : PageModel
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
            LogRemoteError(remoteError);
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }
        
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            LogExternalLoginInfoNotFound();
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        // Extract AMR claims from external provider
        var externalAmrClaims = info.Principal.FindAll(AuthConstants.ClaimTypes.Amr)
            .Select(c => c.Value)
            .ToList();

        // Check if external provider performed MFA
        bool externalMfaPerformed = externalAmrClaims.Contains(AuthConstants.Amr.Mfa) || 
                                    externalAmrClaims.Contains(AuthConstants.Amr.Otp) ||
                                    externalAmrClaims.Contains(AuthConstants.Amr.HardwareKey);

        // Build our AMR claim list
        var amrClaims = new List<Claim>
        {
            new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.External)
        };

        // Add external provider's AMR claims
        foreach (var amr in externalAmrClaims)
        {
            amrClaims.Add(new Claim(AuthConstants.ClaimTypes.Amr, amr));
        }

        // Sign in the user with this external login provider if the user already has a login.
        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            // User already exists and is linked, update their authentication cookie with AMR
            var user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user != null)
            {
                // Re-sign in with AMR claims
                await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, amrClaims);
                
                // Store AMR in session for MFA enforcement logic
                if (externalMfaPerformed)
                {
                    var sessionAmr = new List<string> { AuthConstants.Amr.External, AuthConstants.Amr.Mfa };
                    HttpContext.Session.SetString("AuthenticationMethods", 
                        JsonSerializer.Serialize(sessionAmr));
                }
            }
            
            LogExternalLoginSuccess(info.Principal.Identity?.Name ?? "Unknown", info.LoginProvider, string.Join(", ", externalAmrClaims));
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
                        // Sign in with AMR claims
                        await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, amrClaims);
                        
                        // Store AMR in session
                        if (externalMfaPerformed)
                        {
                            var sessionAmr = new List<string> { AuthConstants.Amr.External, AuthConstants.Amr.Mfa };
                            HttpContext.Session.SetString("AuthenticationMethods", 
                                JsonSerializer.Serialize(sessionAmr));
                        }
                        
                        LogAutoLinkSuccess(email, info.LoginProvider, string.Join(", ", externalAmrClaims));
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

    #region LoggerMessage Methods

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error from external provider: {RemoteError}")]
    partial void LogRemoteError(string remoteError);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error loading external login information.")]
    partial void LogExternalLoginInfoNotFound();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Name} logged in with {LoginProvider} provider. AMR: {Amr}")]
    partial void LogExternalLoginSuccess(string name, string loginProvider, string amr);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-linked {Email} to external login {Provider}. AMR: {Amr}")]
    partial void LogAutoLinkSuccess(string email, string provider, string amr);

    #endregion
}
