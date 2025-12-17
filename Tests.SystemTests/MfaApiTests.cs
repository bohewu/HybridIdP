using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Domain.Constants;
using OtpNet;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for MFA API endpoints.
/// Uses seeded testuser@hybridauth.local with password flow (testclient-public).
/// </summary>
[Collection("MFA Tests")]
public partial class MfaApiTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private record MfaVerifyResponse
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public List<string>? RecoveryCodes { get; init; }
    }

    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private string? _userToken;

    // Use seeded admin user from UserSeeder (same as UserinfoFlowTests)
    private const string TEST_USER_EMAIL = "admin@hybridauth.local";
    private const string TEST_USER_PASSWORD = "Admin@123";

    public MfaApiTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        await Task.Delay(100);
        
        // Get token for seeded test user using password flow with testclient-public
        _userToken = await GetUserTokenAsync(TEST_USER_EMAIL, TEST_USER_PASSWORD);
    }

    public async Task DisposeAsync()
    {
        // Ensure MFA is disabled after all tests in this class (cleanup for test isolation)
        try
        {
            if (_userToken != null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
                
                // Try to disable TOTP MFA
                await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = TEST_USER_PASSWORD });
                
                // Try to disable Email MFA
                await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        
        _httpClient.Dispose();
    }

    [Fact]
    public async Task GetMfaStatus_ValidUser_ReturnsStatus()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.GetAsync("/api/account/mfa/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.NotNull(status);
        Assert.False(status.TwoFactorEnabled); // Should be disabled initially
    }

    [Fact]
    public async Task GetMfaSetup_ValidUser_ReturnsSetupInfo()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.GetAsync("/api/account/mfa/setup");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setup = await response.Content.ReadFromJsonAsync<MfaSetupDto>();
        Assert.NotNull(setup);
        Assert.NotEmpty(setup.SharedKey);
        Assert.Contains("otpauth://totp/", setup.AuthenticatorUri);
        Assert.StartsWith("data:image/png;base64,", setup.QrCodeDataUri);
    }

    [Fact]
    public async Task VerifyMfa_InvalidCode_ReturnsFalse()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
        var invalidCode = "000000";

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { Code = invalidCode });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MfaVerifyResultDto>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_MfaNotEnabled_ReturnsBadRequest()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.PostAsync("/api/account/mfa/recovery-codes", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisableMfa_WrongPassword_ReturnsBadRequest()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = "WrongPassword123!" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MfaEndpoints_Unauthorized_Returns401()
    {
        // Arrange - No auth header
        _httpClient.DefaultRequestHeaders.Authorization = null;

        // Act & Assert
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        
        Assert.Equal(HttpStatusCode.Unauthorized, statusResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, setupResponse.StatusCode);
    }

    [Fact]
    public async Task DisableMfa_ValidPassword_DisablesMfa()
    {
        // First enable MFA
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        var setup = await setupResponse.Content.ReadFromJsonAsync<MfaSetupDto>();
        
        // Generate valid TOTP
        var totp = GenerateTotp(setup!.SharedKey);
        
        var verifyResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { Code = totp });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        // Act - Disable
        var disableResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = TEST_USER_PASSWORD });

        // Assert
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        
        // Verify status is disabled
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(status!.TwoFactorEnabled);
    }

    [Fact]
    public async Task DisableMfa_PasswordlessUser_via_Impersonation()
    {
        // 1. Authenticate as M2M Admin (Client Credentials) to find user
        // M2M token has explicit scopes permissions.
        var m2mToken = await GetM2MAdminTokenAsync();
        
        // 2. Find Passwordless User ID & Verify Admin Role
        var passwordlessUserEmail = "passwordless@hybridauth.local";
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", m2mToken);
        
        // Debug: Check Admin has roles
        var adminSearch = await _httpClient.GetAsync($"/api/admin/users?search={AuthConstants.DefaultAdmin.Email}");
        Assert.Equal(HttpStatusCode.OK, adminSearch.StatusCode);
        var adminResult = await adminSearch.Content.ReadFromJsonAsync<JsonElement>();
        var adminItems = adminResult.GetProperty("items");
        Assert.True(adminItems.GetArrayLength() > 0, "Admin user not found");
        var adminRoles = adminItems[0].GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("Admin", adminRoles); // Assert Admin has Admin role in DB

        var usersResponse = await _httpClient.GetAsync($"/api/admin/users?search={passwordlessUserEmail}");
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode); 
        var usersResult = await usersResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = usersResult.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "No passwordless user found"); 
        var userId = items[0].GetProperty("id").GetString();

        // 3. Authenticate as Admin User (Password Grant) for Impersonation
        // Impersonate requires a real user, not M2M.
        var adminUserToken = await GetUserTokenAsync(AuthConstants.DefaultAdmin.Email, AuthConstants.DefaultAdmin.Password);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminUserToken);

        // 4. Start Impersonation & Capture Cookie
        var impersonateResponse = await _httpClient.PostAsync($"/api/admin/users/{userId}/impersonate", null);
        if (impersonateResponse.StatusCode != HttpStatusCode.OK)
        {
            var error = await impersonateResponse.Content.ReadAsStringAsync();
            Assert.Fail($"Impersonate failed: {impersonateResponse.StatusCode} {error}");
        }
        Assert.Equal(HttpStatusCode.OK, impersonateResponse.StatusCode);
        
        var cookieHeaders = impersonateResponse.Headers.GetValues("Set-Cookie");
        Assert.NotEmpty(cookieHeaders);
        
        // Extract all cookies (name=value) and join them
        var cookies = cookieHeaders.Select(h => h.Split(';')[0]).ToList();
        var cookieHeader = string.Join("; ", cookies); 

        // 5. Setup MFA as Impersonated User (Using Cookie)
        var userClient = new HttpClient(new HttpClientHandler { UseCookies = false, ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }) 
        { 
            BaseAddress = _httpClient.BaseAddress 
        };
        userClient.DefaultRequestHeaders.Add("Cookie", cookieHeader);

        var setupResponse = await userClient.GetAsync("/api/account/mfa/setup");
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setup = await setupResponse.Content.ReadFromJsonAsync<MfaSetupDto>();

        // 6. Verify & Enable MFA
        var totpCodeVerify = GenerateTotp(setup!.SharedKey); 
        var verifyResponse = await userClient.PostAsJsonAsync("/api/account/mfa/verify", new { Code = totpCodeVerify });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<MfaVerifyResponse>();
        Assert.True(verifyResult!.Success, "MFA Verify failed: " + verifyResult.Error);

        // REFRESH IMPERSONATION: Enabling 2FA updates SecurityStamp, invalidating the old cookie.
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminUserToken);
        var refreshImpersonationResponse = await _httpClient.PostAsync($"/api/admin/users/{userId}/impersonate", null);
        Assert.Equal(HttpStatusCode.OK, refreshImpersonationResponse.StatusCode);
        var refreshCookieHeader = refreshImpersonationResponse.Headers.GetValues("Set-Cookie");
        var distinctCookies = refreshCookieHeader.Select(h => h.Split(';')[0]).ToList();
        var refreshedCookieString = string.Join("; ", distinctCookies);

        // Update user client with new cookie
        userClient.DefaultRequestHeaders.Remove("Cookie");
        userClient.DefaultRequestHeaders.Add("Cookie", refreshedCookieString);

        // 7. Disable MFA (Passwordless Flow)
        var totpCodeDisable = GenerateTotp(setup!.SharedKey, offsetSeconds: 30); 
        
        var disableResponse = await userClient.PostAsJsonAsync("/api/account/mfa/disable", new { TotpCode = totpCodeDisable });
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        var disableResult = await disableResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(disableResult.GetProperty("success").GetBoolean());
    }

    #region Email MFA Tests (Phase 20.3)

    [Fact]
    public async Task GetMfaStatus_IncludesEmailMfaEnabled()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.GetAsync("/api/account/mfa/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.NotNull(status);
        Assert.False(status.EmailMfaEnabled); // Should be disabled initially
    }

    [Fact]
    public async Task EmailMfa_EnableDisable_Works()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act - Enable
        var enableResponse = await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        // Verify enabled
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.True(status!.EmailMfaEnabled);

        // Act - Disable
        var disableResponse = await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        // Verify disabled
        var statusResponse2 = await _httpClient.GetAsync("/api/account/mfa/status");
        var status2 = await statusResponse2.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(status2!.EmailMfaEnabled);
    }

    [Fact]
    public async Task EmailMfa_SendCode_ReturnsSuccess()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.PostAsync("/api/account/mfa/email/send", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task EmailMfa_VerifyInvalidCode_ReturnsFalse()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // First send code
        await _httpClient.PostAsync("/api/account/mfa/email/send", null);

        // Act - Verify with invalid code
        var response = await _httpClient.PostAsJsonAsync("/api/account/mfa/email/verify", new { Code = "000000" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.GetProperty("success").GetBoolean());
    }

    #endregion
    
    private async Task<string> GetM2MAdminTokenAsync()
    {
        var scopes = new[]
        {
            "users.read", "users.impersonate"
        };

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024",
            ["scope"] = string.Join(" ", scopes)
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }


    #region Helper Methods

    private string GenerateTotp(string secretKey, int offsetSeconds = 0)
    {
        var bytes = Base32Encoding.ToBytes(secretKey.Replace(" ", ""));
        var totp = new Totp(bytes);
        
        if (offsetSeconds == 0)
            return totp.ComputeTotp(); // Current time
        
        // Compute for future/past
        return totp.ComputeTotp(DateTime.UtcNow.AddSeconds(offsetSeconds));
    }

    private async Task<string> GetUserTokenAsync(string username, string password)
    {
        // Use testclient-public which has password flow enabled
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "testclient-public",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid profile roles"
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Token request failed with {response.StatusCode}: {errorContent}");
        }
        
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }

    #endregion

    #region DTOs

    private record MfaStatusDto
    {
        public bool TwoFactorEnabled { get; init; }
        public bool HasAuthenticator { get; init; }
        public int RecoveryCodesLeft { get; init; }
        public bool HasPassword { get; init; }
        public bool EmailMfaEnabled { get; init; }
    }

    private record MfaSetupDto
    {
        public string SharedKey { get; init; } = "";
        public string AuthenticatorUri { get; init; } = "";
        public string QrCodeDataUri { get; init; } = "";
    }

    private record MfaVerifyResultDto
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public List<string>? RecoveryCodes { get; init; }
    }

    #endregion
}
