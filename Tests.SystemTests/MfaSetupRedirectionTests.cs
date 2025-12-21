using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Xunit;

namespace Tests.SystemTests;

public class MfaSetupRedirectionTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    // Use a standard user who doesn't have MFA enabled by default
    private const string TEST_USER_EMAIL = "testuser@hybridauth.local";
    private const string TEST_USER_PASSWORD = "Password123!";
    
    // Admin credentials for policy updates
    private const string ADMIN_EMAIL = AuthConstants.DefaultAdmin.Email;
    private const string ADMIN_PASSWORD = AuthConstants.DefaultAdmin.Password;

    public MfaSetupRedirectionTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false, // Critical: We want to intercept the redirect
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
    public async Task Login_WithMandatoryMfaEnabled_ShouldRedirectToMfaSetup_AndSetTwoFactorCookie()
    {
        // 1. Authenticate as Service (M2M) to configure policy
        // This avoids issues where Admin user themselves might be forced into MFA setup
        var m2mToken = await GetM2MAdminTokenAsync();
        
        using var apiClient = new HttpClient 
        { 
            BaseAddress = _httpClient.BaseAddress 
        };
        apiClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", m2mToken);

        // 2. Enable Mandatory MFA Policy
        var policyResponse = await apiClient.GetAsync("/api/admin/security/policies");
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<SecurityPolicyDto>();
        
        // Save original state to restore later
        var originalEnforcement = policy!.EnforceMandatoryMfaEnrollment;
        
        try
        {
            if (!originalEnforcement || policy.MfaEnforcementGracePeriodDays != 0)
            {
                policy.EnforceMandatoryMfaEnrollment = true;
                policy.MfaEnforcementGracePeriodDays = 0; // Force immediate redirection
                policy.EnableTotpMfa = true; 
                
                var updateResponse = await apiClient.PutAsJsonAsync("/api/admin/security/policies", policy);
                updateResponse.EnsureSuccessStatusCode();
            }

            // 3. Perform User Login with seeded user for MFA enforcement testing
            // User: mfa-enforce@hybridauth.local / Test@123 (Seeded by UserSeeder)
            var testUserEmail = "mfa-enforce@hybridauth.local";
            var testUserPwd = "Test@123";

            // Ensure seeded user has NO MFA enabled (idempotency check)
            // While seeded data usually is clean, verifying/clearing it via API ensures robustness
            // But API requires auth. Given it's a seeded user for this exact purpose, 
            // we assume its state is correct OR we just proceed to login.
            
            // Use the main _httpClient which has AllowAutoRedirect = false
            var (userToken, _) = await GetLoginPageAsync(_httpClient);
            var userFormData = CreateLoginForm(testUserEmail, testUserPwd, userToken);
            
            var loginResponse = await _httpClient.PostAsync("/Account/Login", userFormData);
            var loginContent = await loginResponse.Content.ReadAsStringAsync();

            // 4. Assert Redirection
            if (loginResponse.StatusCode != HttpStatusCode.Redirect)
            {
                Assert.Fail($"User Login Failed (Expected Redirect, got {loginResponse.StatusCode}). Content: {loginContent}");
            }
            var location = loginResponse.Headers.Location?.ToString();
            if (location == null || !location.Contains("MfaSetup"))
            {
                    // Capture redirect loop or unexpected success
                    Assert.Fail($"Expected redirect to MfaSetup, but got: {location}. Status: {loginResponse.StatusCode}");
            }

            // 5. Assert TwoFactorUserId Cookie
            var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
            var twoFactorCookie = cookies.FirstOrDefault(c => c.Contains("Identity.TwoFactorUserId"));
            
            Assert.NotNull(twoFactorCookie);
            Assert.Contains("Identity.TwoFactorUserId", twoFactorCookie);
        }
        finally
        {
            // 6. Cleanup: Restore original policy
            if (policy != null)
            {
                policy.EnforceMandatoryMfaEnrollment = originalEnforcement;
                await apiClient.PutAsJsonAsync("/api/admin/security/policies", policy);
            }
        }
    }

    private async Task<string> GetM2MAdminTokenAsync()
    {
        var scopes = new[] { "security.policies.write", "security.policies.read" }; // Adjust scopes as needed

        // Note: In a real environment, you'd check what scopes 'testclient-admin' allows.
        // Assuming testclient-admin has broad access or we use a client that does.
        // MfaApiTests uses 'testclient-admin'.
        
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024",
            ["scope"] = "openid profile roles settings.read settings.update" 
        });

        // Use a clean client for token request
        using var tokenClient = new HttpClient { BaseAddress = _httpClient.BaseAddress };
        var response = await tokenClient.PostAsync("/connect/token", tokenRequest);
        
        if (!response.IsSuccessStatusCode)
        {
             var error = await response.Content.ReadAsStringAsync();
             throw new Exception($"M2M Token Request Failed: {response.StatusCode} {error}");
        }
        
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }

    #region Helper Methods

    private async Task<(string token, string html)> GetLoginPageAsync(HttpClient client)
    {
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
