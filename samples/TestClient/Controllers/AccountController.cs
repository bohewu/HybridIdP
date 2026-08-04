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

    private const string ClientId = "testclient-public";
    private const string IdpBaseUrl = "https://localhost:7035";

    public AccountController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        // Get the access token and id_token
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        var idToken = await HttpContext.GetTokenAsync("id_token");
        var refreshToken = await HttpContext.GetTokenAsync("refresh_token");
        
        ViewData["AccessToken"] = accessToken;
        ViewData["IdToken"] = idToken;
        ViewData["RefreshToken"] = refreshToken;
        
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
            var response = await client.GetAsync($"{IdpBaseUrl}/connect/userinfo");
            
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

    [Authorize]
    public async Task<IActionResult> RefreshUserInfo()
    {
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        var refreshToken = await HttpContext.GetTokenAsync("refresh_token");

        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
        {
            ViewData["Success"] = false;
            ViewData["ErrorMessage"] = "Missing access token or refresh token. Ensure offline_access scope is granted.";
            return View("TestApiCall");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            var refreshRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken
            });

            var refreshResponse = await client.PostAsync($"{IdpBaseUrl}/connect/token", refreshRequest);
            var refreshContent = await refreshResponse.Content.ReadAsStringAsync();

            if (!refreshResponse.IsSuccessStatusCode)
            {
                ViewData["Success"] = false;
                ViewData["ErrorMessage"] = $"Refresh token request failed: {refreshResponse.StatusCode} - {refreshContent}";
                return View("TestApiCall");
            }

            var refreshJson = JsonSerializer.Deserialize<JsonElement>(refreshContent);
            if (!refreshJson.TryGetProperty("access_token", out var refreshedTokenElement))
            {
                ViewData["Success"] = false;
                ViewData["ErrorMessage"] = "Refresh token response missing access_token.";
                return View("TestApiCall");
            }

            var refreshedAccessToken = refreshedTokenElement.GetString();
            if (string.IsNullOrEmpty(refreshedAccessToken))
            {
                ViewData["Success"] = false;
                ViewData["ErrorMessage"] = "Refresh token response contained empty access_token.";
                return View("TestApiCall");
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshedAccessToken);
            var userInfoResponse = await client.GetAsync($"{IdpBaseUrl}/connect/userinfo");

            if (userInfoResponse.IsSuccessStatusCode)
            {
                var userInfoContent = await userInfoResponse.Content.ReadAsStringAsync();
                var userInfo = JsonSerializer.Deserialize<JsonElement>(userInfoContent);

                ViewData["Success"] = true;
                ViewData["UserInfo"] = userInfo.GetRawText();
                ViewData["AccessToken"] = refreshedAccessToken;
                ViewData["RefreshTokenFlow"] = true;
            }
            else
            {
                ViewData["Success"] = false;
                ViewData["ErrorMessage"] = $"UserInfo call failed: {userInfoResponse.StatusCode} - {await userInfoResponse.Content.ReadAsStringAsync()}";
            }
        }
        catch (Exception ex)
        {
            ViewData["Success"] = false;
            ViewData["ErrorMessage"] = $"Exception: {ex.Message}";
        }

        return View("TestApiCall");
    }

    public IActionResult Logout()
    {
        return SignOut(new AuthenticationProperties
        {
            RedirectUri = "/"
        }, AuthenticationSchemes.Cookies, AuthenticationSchemes.OpenIdConnect);
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

    public IActionResult InvalidScopes()
    {
        // Always trigger a fresh OpenID Connect challenge so Program.cs can inject
        // the intentionally invalid scope, including when a local session exists.
        return Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            AuthenticationSchemes.OpenIdConnect);
    }

    public IActionResult LoginMfa()
    {
        // Challenge with acr_values=mfa
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = "/Account/Profile",
            Items =
            {
                { "acr_values", "mfa" }
            }
        }, AuthenticationSchemes.OpenIdConnect);
    }
}
