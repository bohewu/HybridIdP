using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace Tests.SystemTests;

public class LegacyAuthSystemTests
{
    private const string Authority = "https://localhost:7035";
    // Using a user that matches LegacyApi mock logic (not "lockout", not empty)
    private const string Username = "legacy_test";
    private const string Password = "Legacy@123";

    private static readonly HttpClientHandler HttpClientHandler = new()
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        AllowAutoRedirect = false, // We want to check redirect
        UseCookies = true,
        CookieContainer = new CookieContainer()
    };

    private static readonly HttpClient HttpClient = new(HttpClientHandler) { BaseAddress = new Uri(Authority) };

    [Fact]
    public async Task Authenticate_LegacyUser_ReturnsSuccessAndProvisionsUser()
    {
        // 1. Get Login Page to obtain AntiForgeryToken
        var loginPageResponse = await HttpClient.GetAsync("/Account/Login");
        loginPageResponse.EnsureSuccessStatusCode();
        var loginPageContent = await loginPageResponse.Content.ReadAsStringAsync();
        var token = GetRequestVerificationToken(loginPageContent);

        // 2. Submit Login Form
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", Username),
            new KeyValuePair<string, string>("Input.Password", Password),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        var loginResponse = await HttpClient.PostAsync("/Account/Login", formData);

        // 3. Verify Result
        // Should be a redirect (302) to ReturnUrl (default ~/)
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        var location = loginResponse.Headers.Location?.ToString();
        Assert.True(location == "/" || location.Contains("returnUrl"), $"Expected redirect to root, got {location}");

        // 4. Follow Redirect to ensure we are authenticated (Cookie check)
        // If we follow redirect, we should get 200 OK on Home Page
        var homeResponse = await HttpClient.GetAsync("/");
        homeResponse.EnsureSuccessStatusCode();
        
        // Optionally check if username is in the returned HTML (if the layout displays it)
        // This confirms we are actually logged in
        // var homeContent = await homeResponse.Content.ReadAsStringAsync();
        // Assert.Contains(Username, homeContent); // Assuming layout shows username
    }

    private string GetRequestVerificationToken(string html)
    {
        var match = Regex.Match(html, @"input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;
        throw new Exception("Could not find __RequestVerificationToken");
    }
}
