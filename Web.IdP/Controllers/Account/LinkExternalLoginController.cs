using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication;
using Core.Domain;
using Web.IdP.Services;
using Web.IdP.Helpers;

namespace Web.IdP.Controllers.Account;

[Authorize]
[Route("Account/[controller]")]
public partial class LinkExternalLoginController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LinkExternalLoginController> _logger;
    private readonly Core.Application.ILoginService _loginService;
    private readonly IExternalSignInCoordinator _externalSignInCoordinator;

    public LinkExternalLoginController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<LinkExternalLoginController> logger,
        Core.Application.ILoginService loginService,
        IExternalSignInCoordinator externalSignInCoordinator)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _loginService = loginService;
        _externalSignInCoordinator = externalSignInCoordinator;
    }

    [HttpGet("Challenge")]
    public async Task<IActionResult> Challenge(string provider, string? operation = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || operation == null ||
            !await AccountSecurityOperationSession.IsAuthorizedAsync(HttpContext, user,
                AccountSecurityOperationSession.ExternalLinkPurpose, provider, operation))
            return Redirect("/Account/Profile?error=FreshAuthenticationRequired");
        PendingExternalLoginLink.Cancel(HttpContext);
        // Request a redirect to the external login provider to link a login for the current user
        var redirectUrl = Url.Action("Callback", "LinkExternalLogin");
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, _userManager.GetUserId(User));
        properties.Items[AccountSecurityOperationSession.CorrelationProperty] = operation;
        return new ChallengeResult(provider, properties);
    }

    [HttpPost("Reauthenticate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reauthenticate(string provider, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();
        if (!(await _signInManager.GetExternalAuthenticationSchemesAsync()).Any(scheme => scheme.Name == provider))
            return BadRequest(new { error = "ExternalLoginFailed" });
        var nonce = await AccountSecurityOperationSession.BeginAsync(HttpContext, user,
            AccountSecurityOperationSession.ExternalLinkPurpose, provider, cancellationToken);
        if (nonce == null) return StatusCode(403, new { error = "freshAuthenticationRequired" });
        var continuation = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            "/Account/LinkExternalLogin/Challenge", new Dictionary<string, string?>
            { ["provider"] = provider, ["operation"] = nonce });
        return Ok(new { loginUrl = await AccountSecurityOperationSession.GetLoginUrlAsync(HttpContext, user, continuation) });
    }

    [HttpGet("Callback")]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Redirect("/"); // Should not happen due to [Authorize]
        }

        var expectedXsrf = user.Id.ToString();
        var info = await _signInManager.GetExternalLoginInfoAsync(expectedXsrf);
        if (info == null)
        {
            LogExternalLoginInfoNotFound(); // Modified: Removed user.Id
            return Redirect("/Account/Profile?error=ExternalLoginFailed"); // Modified: Error message
        }

        string? operation = null;
        info.AuthenticationProperties?.Items.TryGetValue(AccountSecurityOperationSession.CorrelationProperty, out operation);
        if (operation == null || !await AccountSecurityOperationSession.IsAuthorizedAsync(HttpContext, user,
                AccountSecurityOperationSession.ExternalLinkPurpose, info.LoginProvider, operation))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Redirect("/Account/Profile?error=FreshAuthenticationRequired");
        }
        // Check MaxLoginsPerProvider limit
        var linkCheck = await _loginService.CanLinkExternalLoginAsync(user, info.LoginProvider, cancellationToken);
        if (!linkCheck.Succeeded)
        {
             return Redirect("/Account/Profile?error=ProviderLimitReached");
        }

        var result = await _externalSignInCoordinator.LinkAsync(HttpContext, user, info, cancellationToken);
        if (!result.IsSucceeded)
        {
            const string returnUrl = "/Account/Profile?success=LinkAdded";
            return result.Status switch
            {
                ExternalSignInCompletionStatus.TotpRequired => RedirectToPage("/Account/LoginTotp", new { returnUrl }),
                ExternalSignInCompletionStatus.EmailOtpRequired => RedirectToPage("/Account/LoginEmailOtp", new { returnUrl }),
                ExternalSignInCompletionStatus.PasskeyRequired => RedirectToPage("/Account/LoginMfa", new { returnUrl }),
                ExternalSignInCompletionStatus.MfaEnrollmentRequired => RedirectToPage("/Account/MfaSetup", new { returnUrl }),
                _ => Redirect("/Account/Profile?error=LinkFailed")
            };
        }

        // Clear the external authentication cookie to ensure a clean state
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        LogExternalLoginLinked(user.Id, info.LoginProvider);
        return Redirect("/Account/Profile?success=LinkAdded");
    }

    #region LoggerMessage Methods

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error loading external login information.")]
    partial void LogExternalLoginInfoNotFound();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to add login for user {UserId}: {LoginProvider}")]
    partial void LogAddLoginFailed(Guid userId, string loginProvider);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} linked {LoginProvider} account.")]
    partial void LogExternalLoginLinked(Guid userId, string loginProvider);

    #endregion
}
