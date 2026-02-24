using System.Security.Claims;
using System.Text.Json;
using Core.Domain.Entities;
using Core.Domain.Constants;
using Core.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.IdP.Infrastructure.Identity;
using Core.Application.Options;
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
    private readonly Core.Application.ILoginService _loginService;
    private readonly Core.Application.IUserManagementService _userManagementService;
    private readonly Core.Application.ILoginHistoryService _loginHistoryService;

    public ExternalLoginCallbackModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<ExternalLoginCallbackModel> logger,
        IOptions<ExternalLoginOptions> externalLoginOptions,
        Core.Application.ILoginService loginService,
        Core.Application.IUserManagementService userManagementService,
        Core.Application.ILoginHistoryService loginHistoryService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _externalLoginOptions = externalLoginOptions.Value;
        _loginService = loginService;
        _userManagementService = userManagementService;
        _loginHistoryService = loginHistoryService;
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
                var eligibility = await _loginService.ValidateExternalUserSignInAsync(user);
                if (!eligibility.IsSuccess)
                {
                    return HandleExternalSignInBlocked(user, eligibility);
                }
                
                // Re-sign in with AMR claims
                await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, amrClaims);
                await _userManagementService.UpdateLastLoginAsync(user.Id);
                await RecordSuccessfulLoginAsync(user.Id);
                
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
                    // Security Check: Enforce MaxLoginsPerProvider
                    var checkResult = await _loginService.CanLinkExternalLoginAsync(user, info.LoginProvider);
                    bool canLink = checkResult.Succeeded;
                    
                    if (!canLink)
                    {
                        _logger.LogWarning("Auto-link skipped for user {UserId} and provider {Provider} due to limit check: {Error}", user.Id, info.LoginProvider, checkResult.Error);
                    }

                    if (canLink)
                    {
                        // Confirm email is confirmed? (Optional security check, usually external email is trusted if email_verified claim is true)
                        // For now, if config allows, we link.
                        var addLoginResult = await _userManager.AddLoginAsync(user, info);
                        if (addLoginResult.Succeeded)
                        {
                            var eligibility = await _loginService.ValidateExternalUserSignInAsync(user);
                            if (!eligibility.IsSuccess)
                            {
                                // Remove the login we just added since user is not eligible to sign in
                                await _userManager.RemoveLoginAsync(user, info.LoginProvider, info.ProviderKey);
                                return HandleExternalSignInBlocked(user, eligibility);
                            }
                            
                            // Sign in with AMR claims
                            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, amrClaims);
                            await _userManagementService.UpdateLastLoginAsync(user.Id);
                            await RecordSuccessfulLoginAsync(user.Id);
                            
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
        }

        // If we get here, the user is new or not linked. Redirect to confirmation page.
        // pass ReturnUrl
        // We need to store returnUrl in ViewData or pass it to next page

        return RedirectToPage("./ExternalLoginConfirmation", new { ReturnUrl = returnUrl });
    }

    private IActionResult HandleExternalSignInBlocked(ApplicationUser user, LoginResult eligibility)
    {
        if (eligibility.Status == LoginStatus.LockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        var reason = eligibility.Message ?? eligibility.Status.ToString();
        LogPersonInactive(user.Email ?? user.UserName!, reason);

        var error = eligibility.Status switch
        {
            LoginStatus.UserInactive => "UserInactive",
            LoginStatus.PersonInactive => "PersonInactive",
            _ => "ExternalLoginFailure"
        };

        return RedirectToPage("./Login", new { error, message = reason });
    }

    private async Task RecordSuccessfulLoginAsync(Guid userId)
    {
        try
        {
            var loginHistory = new LoginHistory
            {
                UserId = userId,
                LoginTime = DateTime.UtcNow,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                IsSuccessful = true,
                RiskScore = 0,
                IsFlaggedAbnormal = false
            };

            loginHistory.IsFlaggedAbnormal = await _loginHistoryService.DetectAbnormalLoginAsync(loginHistory);
            await _loginHistoryService.RecordLoginAsync(loginHistory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record external login history for user {UserId}", userId);
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "External login blocked for {Email} due to Person status: {Reason}")]
    partial void LogPersonInactive(string email, string reason);

    #endregion
}
