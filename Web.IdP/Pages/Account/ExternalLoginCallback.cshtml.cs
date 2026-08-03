using System.Security.Claims;
using Core.Domain.Entities;
using Core.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Core.Application.Options;
using Microsoft.Extensions.Options;
using Core.Domain;
using Web.IdP.Infrastructure.Identity;
using Web.IdP.Services;

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
    private readonly IExternalSignInCoordinator _externalSignInCoordinator;

    public ExternalLoginCallbackModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<ExternalLoginCallbackModel> logger,
        IOptions<ExternalLoginOptions> externalLoginOptions,
        Core.Application.ILoginService loginService,
        Core.Application.IUserManagementService userManagementService,
        Core.Application.ILoginHistoryService loginHistoryService,
        IExternalSignInCoordinator externalSignInCoordinator)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _externalLoginOptions = externalLoginOptions.Value;
        _loginService = loginService;
        _userManagementService = userManagementService;
        _loginHistoryService = loginHistoryService;
        _externalSignInCoordinator = externalSignInCoordinator;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? remoteError = null, CancellationToken cancellationToken = default)
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

        var linkedUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (linkedUser != null)
        {
            var completion = await _externalSignInCoordinator.CompleteAsync(
                HttpContext,
                linkedUser,
                cancellationToken);
            if (!completion.IsSucceeded)
            {
                return HandleExternalSignInIncomplete(linkedUser, completion, returnUrl);
            }

            await _userManagementService.UpdateLastLoginAsync(linkedUser.Id, cancellationToken);
            await RecordSuccessfulLoginAsync(linkedUser.Id);

            LogExternalLoginSuccess(info.Principal.Identity?.Name ?? "Unknown", info.LoginProvider);
            return LocalRedirect(returnUrl);
        }

        // If the user does not have an account, then ask the user to create an account.
        // CHECK AUTO-LINK: If configured AND email matches exactly.
        if (_externalLoginOptions.AutoLinkMatchingEmail && ExternalEmailAssurance.IsVerified(info))
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (!string.IsNullOrEmpty(email))
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user != null)
                {
                    // Security Check: Enforce MaxLoginsPerProvider
                    var checkResult = await _loginService.CanLinkExternalLoginAsync(user, info.LoginProvider, cancellationToken);
                    bool canLink = checkResult.Succeeded;
                    
                    if (!canLink)
                    {
                        _logger.LogWarning("Auto-link skipped for user {UserId} and provider {Provider} due to limit check: {Error}", user.Id, info.LoginProvider, checkResult.Error);
                    }

                    if (canLink)
                    {
                        var addLoginResult = await _userManager.AddLoginAsync(user, info);
                        if (addLoginResult.Succeeded)
                        {
                            var completion = await _externalSignInCoordinator.CompleteAsync(
                                HttpContext,
                                user,
                                cancellationToken);
                            if (completion.Status == ExternalSignInCompletionStatus.Blocked)
                            {
                                await _userManager.RemoveLoginAsync(user, info.LoginProvider, info.ProviderKey);
                                return HandleExternalSignInIncomplete(user, completion, returnUrl);
                            }

                            if (!completion.IsSucceeded)
                            {
                                return HandleExternalSignInIncomplete(user, completion, returnUrl);
                            }

                            await _userManagementService.UpdateLastLoginAsync(user.Id, cancellationToken);
                            await RecordSuccessfulLoginAsync(user.Id);

                            LogAutoLinkSuccess(email, info.LoginProvider);
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

    private IActionResult HandleExternalSignInIncomplete(
        ApplicationUser user,
        ExternalSignInCompletionResult completion,
        string returnUrl)
    {
        return completion.Status switch
        {
            ExternalSignInCompletionStatus.TotpRequired =>
                RedirectToPage("./LoginTotp", new { returnUrl, rememberMe = false }),
            ExternalSignInCompletionStatus.EmailOtpRequired =>
                RedirectToPage("./LoginEmailOtp", new { returnUrl, rememberMe = false }),
            ExternalSignInCompletionStatus.MfaEnrollmentRequired =>
                RedirectToPage("./MfaSetup", new { returnUrl }),
            ExternalSignInCompletionStatus.Blocked when completion.Denial != null =>
                HandleExternalSignInBlocked(user, completion.Denial),
            _ => RedirectToPage("./Login", new { ReturnUrl = returnUrl, error = "ExternalLoginFailure" })
        };
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

    [LoggerMessage(Level = LogLevel.Information, Message = "{Name} logged in with {LoginProvider} provider.")]
    partial void LogExternalLoginSuccess(string name, string loginProvider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-linked {Email} to external login {Provider}.")]
    partial void LogAutoLinkSuccess(string email, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "External login blocked for {Email} due to Person status: {Reason}")]
    partial void LogPersonInactive(string email, string reason);

    #endregion
}
