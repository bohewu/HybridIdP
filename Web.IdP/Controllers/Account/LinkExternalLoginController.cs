using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Core.Domain.Entities;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication; // Added for SignOutAsync
using Core.Domain; // For ApplicationUser
using Core.Domain.Constants; // For AuthConstants
using Microsoft.Extensions.Options; // Added
using Core.Application.Options; // Added

namespace Web.IdP.Controllers.Account;

[Authorize]
[Route("Account/[controller]")]
public partial class LinkExternalLoginController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LinkExternalLoginController> _logger;
    private readonly ExternalLoginOptions _externalLoginOptions;
    private readonly Core.Application.ILoginService _loginService;

    public LinkExternalLoginController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<LinkExternalLoginController> logger,
        IOptions<ExternalLoginOptions> externalLoginOptions,
        Core.Application.ILoginService loginService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _externalLoginOptions = externalLoginOptions.Value;
        _loginService = loginService;
    }

    [HttpGet("Challenge")]
    public IActionResult Challenge(string provider)
    {
        // Request a redirect to the external login provider to link a login for the current user
        var redirectUrl = Url.Action("Callback", "LinkExternalLogin");
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, _userManager.GetUserId(User));
        return new ChallengeResult(provider, properties);
    }

    [HttpGet("Callback")]
    public async Task<IActionResult> Callback()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Redirect("/"); // Should not happen due to [Authorize]
        }

        var info = await _signInManager.GetExternalLoginInfoAsync(); // Modified: Removed user.Id.ToString()
        if (info == null)
        {
            LogExternalLoginInfoNotFound(); // Modified: Removed user.Id
            return Redirect("/Account/Profile?error=ExternalLoginFailed"); // Modified: Error message
        }

        // Check MaxLoginsPerProvider limit
        var linkCheck = await _loginService.CanLinkExternalLoginAsync(user, info.LoginProvider);
        if (!linkCheck.Succeeded)
        {
             return Redirect("/Account/Profile?error=ProviderLimitReached");
        }

        var result = await _userManager.AddLoginAsync(user, info);
        if (!result.Succeeded)
        {
            // Modified: Simplified error logging and handling
            if (result.Errors.Any(e => e.Code == "LoginAlreadyAssociated"))
            {
                return Redirect("/Account/Profile?error=LoginAlreadyAssociated");
            }
            LogAddLoginFailed(user.Id, info.LoginProvider); // Modified: Changed errors to info.LoginProvider
            return Redirect("/Account/Profile?error=LinkFailed");
        }

        // Extract AMR from external provider
        var externalAmrClaims = info.Principal.FindAll(AuthConstants.ClaimTypes.Amr)
            .Select(c => c.Value)
            .ToList();

        // Update authentication cookie with AMR claims
        var amrClaims = new List<Claim>
        {
            new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.External)
        };

        foreach (var amr in externalAmrClaims)
        {
            amrClaims.Add(new Claim(AuthConstants.ClaimTypes.Amr, amr));
        }

        // Refresh sign-in with new AMR claims
        await _signInManager.RefreshSignInAsync(user);
        await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, amrClaims);

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
