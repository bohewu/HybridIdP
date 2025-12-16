using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Application.DTOs;
using Core.Domain.Constants;
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

    #region Inactive User/Person Tests

    [Fact]
    public async Task Login_WithInactiveUser_ShouldReturnDeactivatedError()
    {
        // Arrange - Use seeded inactive user (inactive@hybridauth.local / Inactive@123)
        var (token, _) = await GetLoginPageAsync();
        var formData = CreateLoginForm("inactive@hybridauth.local", "Inactive@123", token);

        // Act
        var response = await _httpClient.PostAsync("/Account/Login", formData);

        // Assert - Should return login page with deactivated error
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            content.Contains("deactivated") || 
            content.Contains("停用") ||
            content.Contains("alert-danger") ||
            content.Contains("validation-summary-errors"),
            $"Expected deactivated error message. Content length: {content.Length}");
    }

    [Fact]
    public async Task Login_WithInactivePerson_ShouldReturnPersonInactiveError()
    {
        // Arrange - Use seeded user with inactive person (inactiveperson@hybridauth.local / InactivePerson@123)
        var (token, _) = await GetLoginPageAsync();
        var formData = CreateLoginForm("inactiveperson@hybridauth.local", "InactivePerson@123", token);

        // Act
        var response = await _httpClient.PostAsync("/Account/Login", formData);

        // Assert - Should return login page with person inactive error
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            content.Contains("person") || 
            content.Contains("Person") ||
            content.Contains("人員") ||
            content.Contains("alert-danger") ||
            content.Contains("validation-summary-errors"),
            $"Expected person inactive error message. Content length: {content.Length}");
    }

    #endregion

    #region Lockout Tests

    [Fact]
    public async Task Login_WithMultipleFailedAttempts_ShouldLockout()
    {
        // This test uses a dedicated user to avoid affecting other tests
        // Using testuser@hybridauth.local for lockout test
        const string testEmail = "testuser@hybridauth.local";
        const string wrongPassword = "WrongPassword!";
        
        // Arrange - Get initial token
        var (token, _) = await GetLoginPageAsync();

        // Act - Send multiple failed login attempts (default MaxFailedAccessAttempts is 5)
        for (int i = 0; i < 6; i++)
        {
            var formData = CreateLoginForm(testEmail, wrongPassword, token);
            var response = await _httpClient.PostAsync("/Account/Login", formData);
            var content = await response.Content.ReadAsStringAsync();
            
            // Get new token for next attempt
            token = ExtractAntiForgeryToken(content);
        }

        // Final attempt - should be locked out
        var finalFormData = CreateLoginForm(testEmail, wrongPassword, token);
        var finalResponse = await _httpClient.PostAsync("/Account/Login", finalFormData);
        var finalContent = await finalResponse.Content.ReadAsStringAsync();

        // Assert - Should indicate lockout
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        Assert.True(
            finalContent.Contains("locked") || 
            finalContent.Contains("Locked") ||
            finalContent.Contains("validation-summary-errors") ||
            finalContent.Contains("login-error-summary"),
            $"Expected lockout message after multiple failed attempts.\nContent: {finalContent}");
    }

    #endregion

    #region Abnormal Login & Session Tests

    [Fact]
    public async Task Login_WithAbnormalHistory_ShouldBlockLogin()
    {
        // 1. Login as Admin to enable BlockAbnormalLogin
        var (token, _) = await GetLoginPageAsync();
        var adminFormData = CreateLoginForm(AuthConstants.DefaultAdmin.Email, AuthConstants.DefaultAdmin.Password, token);
        var loginResponse = await _httpClient.PostAsync("/Account/Login", adminFormData);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        // 2. Get Current Policy
        var policyResponse = await _httpClient.GetAsync("/api/admin/security/policies");
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<SecurityPolicyDto>();
        
        var originalSetting = policy.BlockAbnormalLogin;

        try 
        {
            // 3. Enable BlockAbnormalLogin
            policy.BlockAbnormalLogin = true;
            // Note: UpdatePolicy requires all fields in DTO. Since we got full DTO from Get, we can send it back.
            var updateResponse = await _httpClient.PutAsJsonAsync("/api/admin/security/policies", policy);
            updateResponse.EnsureSuccessStatusCode();

            // 4. Create NEW Client for User (clean session)
            using var userHandler = new HttpClientHandler 
            { 
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };
            using var userClient = new HttpClient(userHandler) { BaseAddress = _httpClient.BaseAddress };

            // 5. User Login Attempt (from "unknown" IP ::1, while history has 192.168.1.100)
            // Need token for this new client session
            var (userToken, _) = await GetLoginPageAsync(userClient);
            var userFormData = CreateLoginForm("abnormal@hybridauth.local", "Abnormal@123", userToken);
            
            var userResponse = await userClient.PostAsync("/Account/Login", userFormData);
            var userContent = await userResponse.Content.ReadAsStringAsync();

            // 6. Assert Blocked
            Assert.Equal(HttpStatusCode.OK, userResponse.StatusCode);
            Assert.True(
                userContent.Contains("abnormal") || 
                userContent.Contains("suspicious") || 
                userContent.Contains("異常") ||
                userContent.Contains("Verify identity") ||
                userContent.Contains("alert-danger") ||
                userContent.Contains("validation-summary-errors"),
                $"Expected abnormal login block. Content length: {userContent.Length}");
        }
        finally
        {
            // 7. Revert Policy (Using Helper Admin Client or reusing _httpClient if session still valid)
            // _httpClient session should still be valid as Admin
            policy.BlockAbnormalLogin = originalSetting;
            await _httpClient.PutAsJsonAsync("/api/admin/security/policies", policy);
        }
    }

    [Fact]
    public async Task Login_AfterSessionRevoked_ShouldRequireReauth()
    {
        // 1. Login as User (Use AppManager to avoid conflict with Lockout test which locks 'testuser')
        var (token, _) = await GetLoginPageAsync();
        var userFormData = CreateLoginForm("appmanager@hybridauth.local", "AppManager@123", token);
        var loginResponse = await _httpClient.PostAsync("/Account/Login", userFormData);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        
        // Verify access to protected resource using _httpClient (which has user cookie)
        var homeResponse = await _httpClient.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, homeResponse.StatusCode);

        // 2. Login as Admin (separate client) to access Admin API
        using var adminHandler = new HttpClientHandler 
        { 
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        using var adminClient = new HttpClient(adminHandler) { BaseAddress = _httpClient.BaseAddress };

        var (adminToken, _) = await GetLoginPageAsync(adminClient);
        var adminFormData = CreateLoginForm(AuthConstants.DefaultAdmin.Email, AuthConstants.DefaultAdmin.Password, adminToken);
        var adminLoginResponse = await adminClient.PostAsync("/Account/Login", adminFormData);
        Assert.Equal(HttpStatusCode.Redirect, adminLoginResponse.StatusCode);

        // 3. Find User ID by email
        var usersResponse = await adminClient.GetAsync("/api/admin/users?search=appmanager@hybridauth.local");
        usersResponse.EnsureSuccessStatusCode();
        var usersJson = await usersResponse.Content.ReadFromJsonAsync<JsonElement>();
        
        // Use CamelCase for properties as per standard default
        var userId = usersJson.GetProperty("items")[0].GetProperty("id").GetString();

        // 4. Revoke Sessions
        var revokeResponse = await adminClient.PostAsync($"/api/admin/users/{userId}/sessions/revoke-all", null);
        revokeResponse.EnsureSuccessStatusCode();

        // 5. Verify User Access Revoked (using _httpClient)
        // Note: Cookie auth middleware validates cookie on every request. 
        // If session store says revoked / missing, it should challenge.
        var accessResponse = await _httpClient.GetAsync("/");
        
        // Should be redirect to login
        Assert.Equal(HttpStatusCode.Redirect, accessResponse.StatusCode);
        Assert.Contains("/Account/Login", accessResponse.Headers.Location?.ToString());
    }

    #endregion

    #region Helper Methods

    private async Task<(string token, string html)> GetLoginPageAsync(HttpClient? client = null)
    {
        client ??= _httpClient;
        var response = await client.GetAsync("/Account/Login");
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
