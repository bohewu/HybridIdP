using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TestClient.Constants;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TestClient.Controllers;

public class AccountController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AccountController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        // Get the access token
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        ViewData["AccessToken"] = accessToken;
        
        return View();
    }

    [Authorize]
    public async Task<IActionResult> TestApiCall()
    {
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        
        if (string.IsNullOrEmpty(accessToken))
        {
            ViewData["ErrorMessage"] = "No access token found";
            ViewData["Success"] = false;
            return View();
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            
            // Call IdP's /connect/userinfo endpoint
            var response = await client.GetAsync("https://localhost:7035/connect/userinfo");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var userInfo = JsonSerializer.Deserialize<JsonElement>(content);
                
                ViewData["Success"] = true;
                ViewData["UserInfo"] = userInfo.GetRawText();
                ViewData["AccessToken"] = accessToken;
            }
            else
            {
                ViewData["Success"] = false;
                ViewData["ErrorMessage"] = $"API call failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}";
            }
        }
        catch (Exception ex)
        {
            ViewData["Success"] = false;
            ViewData["ErrorMessage"] = $"Exception: {ex.Message}";
        }
        
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

    [Authorize]
    public async Task<IActionResult> InvalidScopes()
    {
        // This action triggers the OpenIdConnect redirect with an injected invalid scope.
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        ViewData["AccessToken"] = accessToken;
        // Reuse the profile view for simplicity.
        return View("Profile");
    }
}
