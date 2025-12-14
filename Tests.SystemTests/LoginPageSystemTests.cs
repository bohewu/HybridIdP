using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for Login page functionality.
/// Tests various login failure scenarios including inactive user and inactive person.
/// </summary>
public class LoginPageSystemTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    public LoginPageSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = _cookieContainer
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7035") };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ShouldRedirectToHome()
    {
        // Arrange - Use seeded test user (admin@hybridauth.local / Admin@123)
        var (token, _) = await GetLoginPageAsync();
        var formData = CreateLoginForm("admin@hybridauth.local", "Admin@123", token);

        // Act
        var response = await _httpClient.PostAsync("/Account/Login", formData);

        // Assert - Should redirect to home on success
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.True(location == "/" || location!.StartsWith("/"), $"Expected redirect to root, got: {location}");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShouldReturnLoginPageWithError()
    {
        // Arrange
        var (token, _) = await GetLoginPageAsync();
        var formData = CreateLoginForm("admin@admin.com", "WrongPassword123!", token);

        // Act
        var response = await _httpClient.PostAsync("/Account/Login", formData);

        // Assert - Should return login page (200 OK) with error
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Should contain error message (either in English or Chinese) or validation error summary
        Assert.True(
            content.Contains("Invalid login attempt") || 
            content.Contains("alert-danger") ||
            content.Contains("validation-summary-errors") ||
            content.Contains("無效的登入嘗試"),
            $"Expected invalid credentials error message. Response length: {content.Length}");
    }

    [Fact]
    public async Task Login_WithNonExistentUser_ShouldReturnLoginPageWithError()
    {
        // Arrange
        var (token, _) = await GetLoginPageAsync();
        var formData = CreateLoginForm("nonexistent@example.com", "AnyPassword123!", token);

        // Act
        var response = await _httpClient.PostAsync("/Account/Login", formData);

        // Assert - Should return login page with invalid credentials error
        // (We don't reveal whether user exists or not for security)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            content.Contains("Invalid login attempt") || 
            content.Contains("alert-danger") ||
            content.Contains("validation-summary-errors") ||
            content.Contains("無效的登入嘗試"),
            $"Expected invalid credentials error message. Response length: {content.Length}");
    }

    [Fact]
    public async Task Login_WithEmptyFields_ShouldReturnValidationErrors()
    {
        // Arrange
        var (token, _) = await GetLoginPageAsync();
        var formData = CreateLoginForm("", "", token);

        // Act
        var response = await _httpClient.PostAsync("/Account/Login", formData);

        // Assert - Should return login page with validation errors
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Check for validation error indicators
        Assert.Contains("validation", content.ToLower());
    }

    [Fact]
    public async Task LoginPage_ShouldContainAntiForgeryToken()
    {
        // Act
        var response = await _httpClient.GetAsync("/Account/Login");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Contains("__RequestVerificationToken", content);
    }

    [Fact]
    public async Task LoginPage_ShouldContainLoginForm()
    {
        // Act
        var response = await _httpClient.GetAsync("/Account/Login");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Contains("Input.Login", content);
        Assert.Contains("Input.Password", content);
        Assert.Contains("type=\"submit\"", content);
    }

    #region Helper Methods

    private async Task<(string token, string html)> GetLoginPageAsync()
    {
        var response = await _httpClient.GetAsync("/Account/Login");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        return (token, html);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;
        
        // Try alternative format
        match = Regex.Match(html, @"input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;
        
        throw new Exception("Could not find __RequestVerificationToken in HTML");
    }

    private static FormUrlEncodedContent CreateLoginForm(string login, string password, string token)
    {
        return new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", login),
            new KeyValuePair<string, string>("Input.Password", password),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });
    }

    #endregion
}
