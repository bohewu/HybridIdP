using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using TestClient.Constants;
using TestClient.Models;
using TestClient.Options;
using TestClient.Services;

namespace TestClient.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class AccountController(OidcDemoService demo, IOptions<OidcDemoOptions> options) : Controller
{
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var session = await HttpContext.AuthenticateAsync(AuthenticationSchemes.Cookies);
        var properties = session.Properties ?? new AuthenticationProperties();
        var configured = options.Value;
        DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt);
        var authTime = long.TryParse(User.FindFirst("auth_time")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
            seconds is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : (DateTimeOffset?)null;
        properties.Items.TryGetValue("demo_refresh_count", out var refreshCountText);
        var count = int.TryParse(refreshCountText, out var refreshCount) ? refreshCount : 0;
        return View(new ProfileViewModel(configured.Authority, configured.ClientId, User.Identity?.Name,
            configured.Scopes, User.Claims.Where(claim => claim.Type is not ("nonce" or "at_hash" or "c_hash"))
                .Select(claim => new ClaimDisplayViewModel(claim.Type, claim.Value)).ToList(),
            User.FindAll("amr").Select(claim => claim.Value).Distinct().ToList(), authTime,
            expiresAt == default ? null : expiresAt,
            new[] { "access_token", "id_token", "refresh_token" }.Select(name =>
                new TokenStatusViewModel(name, !string.IsNullOrEmpty(properties.GetTokenValue(name)))).ToList(), count));
    }

    [Authorize]
    public async Task<IActionResult> TestApiCall(CancellationToken cancellationToken) =>
        View(await demo.ReadUserInfoAsync(await HttpContext.GetTokenAsync("access_token"), cancellationToken));

    [Authorize, HttpPost]
    public async Task<IActionResult> RefreshUserInfo(CancellationToken cancellationToken)
    {
        var session = await HttpContext.AuthenticateAsync(AuthenticationSchemes.Cookies);
        if (!session.Succeeded || session.Principal is null || session.Properties is null)
            return Challenge(AuthenticationSchemes.OpenIdConnect);
        var result = await demo.RefreshAsync(session.Properties, cancellationToken);
        if (!result.Success)
            return View("TestApiCall", new ApiDemoViewModel("Refresh + UserInfo", false, result.Message, result.StatusCode));
        // Save the rotated refresh token even if the following UserInfo call fails.
        await HttpContext.SignInAsync(AuthenticationSchemes.Cookies, session.Principal, session.Properties);
        var userInfo = await demo.ReadUserInfoAsync(session.Properties.GetTokenValue("access_token"), cancellationToken);
        return View("TestApiCall", userInfo with
        {
            Operation = "Refresh + UserInfo",
            Message = result.Message + " " + userInfo.Message,
            RefreshTokenRotated = result.RefreshTokenRotated
        });
    }

    public IActionResult Login(string scenario = "standard")
    {
        if (scenario is not ("standard" or "mfa" or "fresh" or "max-age" or "consent" or "silent" or "invalid-scope"))
            return BadRequest();
        if (scenario == "invalid-scope") return RedirectToAction(nameof(InvalidScopes));
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = "/Account/Profile",
            Items = { ["demo_scenario"] = scenario }
        }, AuthenticationSchemes.OpenIdConnect);
    }

    public IActionResult LoginMfa() => Login("mfa");
    public async Task<IActionResult> InvalidScopes(CancellationToken cancellationToken) =>
        View("TestApiCall", await demo.ProbeInvalidScopeAsync(
            UriHelper.BuildAbsolute(Request.Scheme, Request.Host, Request.PathBase, "/signin-oidc"), cancellationToken));

    [HttpPost]
    public IActionResult Logout() => SignOut(new AuthenticationProperties { RedirectUri = "/" },
        AuthenticationSchemes.Cookies, AuthenticationSchemes.OpenIdConnect);

    public IActionResult AccessDenied() => View();

    public IActionResult AuthError(string? error)
    {
        ViewData["ErrorMessage"] = error is "invalid_scope" or "invalid_request" or "login_required" or
            "consent_required" or "interaction_required" or "server_error" ? error : "authentication_failed";
        return View();
    }
}
