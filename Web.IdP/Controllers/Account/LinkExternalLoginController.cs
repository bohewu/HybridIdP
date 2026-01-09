using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Core.Domain.Entities;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication; // Added for SignOutAsync
using Core.Domain; // For ApplicationUser

namespace Web.IdP.Controllers.Account;

[Authorize]
[Route("Account/[controller]")]
public class LinkExternalLoginController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LinkExternalLoginController> _logger;

    public LinkExternalLoginController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<LinkExternalLoginController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
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

        var info = await _signInManager.GetExternalLoginInfoAsync(user.Id.ToString());
        if (info == null)
        {
            _logger.LogWarning("Could not retrieve external login info during link process for user {UserId}", user.Id);
            return Redirect("/Account/Profile?error=ExternalLoginInfoNotFound");
        }

        var result = await _userManager.AddLoginAsync(user, info);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Failed to add external login for user {UserId}. Errors: {Errors}", user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
            // Check if error is "Login already associated"
            if (result.Errors.Any(e => e.Code == "LoginAlreadyAssociated"))
            {
                return Redirect("/Account/Profile?error=LoginAlreadyAssociated");
            }
            return Redirect("/Account/Profile?error=LinkFailed");
        }

        // Clear the external authentication cookie to ensure a clean state
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        return Redirect("/Account/Profile?success=LinkAdded");
    }
}
