using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TestClient.Constants;

namespace TestClient.Controllers;

public class AccountController : Controller
{
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        // Get the access token
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        ViewData["AccessToken"] = accessToken;
        
        return View();
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthenticationSchemes.Cookies);
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    public IActionResult AuthError(string? error)
    {
        ViewData["ErrorMessage"] = error ?? "An authentication error occurred.";
        return View();
    }
}
