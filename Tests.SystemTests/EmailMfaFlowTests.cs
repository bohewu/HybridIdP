using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for Email MFA (OTP) login flow.
/// Tests the complete Email OTP verification flow during authentication.
/// Marked as Slow due to rate limit waits.
/// Run with: dotnet test --filter "Category!=Slow" to skip.
/// </summary>
[Trait("Category", "Slow")]
public class EmailMfaFlowTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private string? _userToken;

    // Use seeded admin user (has email configured)
    private const string TEST_USER_EMAIL = "admin@hybridauth.local";
    private const string TEST_USER_PASSWORD = "Admin@123";

    public EmailMfaFlowTests(WebIdPServerFixture serverFixture)
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
        await Task.Delay(1000);
        
        // Get token for seeded test user using password flow
        _userToken = await GetUserTokenAsync(TEST_USER_EMAIL, TEST_USER_PASSWORD);
    }

    public async Task DisposeAsync()
    {
        // Cleanup: Ensure Email MFA is disabled after tests
        if (_userToken != null)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
            await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
        }
        _httpClient.Dispose();
    }

    #region Email MFA Enable/Disable Flow

    [Fact(Skip = "Email MFA tests - run separately")]
    public async Task EmailMfa_FullEnableDisableFlow_Works()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // 1. Check initial status - should be disabled
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(status!.EmailMfaEnabled, "Email MFA should be disabled initially");

        // 2. Enable Email MFA
        var enableResponse = await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        // 3. Verify it's enabled
        var statusAfterEnable = await _httpClient.GetAsync("/api/account/mfa/status");
        var enabledStatus = await statusAfterEnable.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.True(enabledStatus!.EmailMfaEnabled, "Email MFA should be enabled after enable call");

        // 4. Disable Email MFA
        var disableResponse = await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        // 5. Verify it's disabled
        var statusAfterDisable = await _httpClient.GetAsync("/api/account/mfa/status");
        var disabledStatus = await statusAfterDisable.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(disabledStatus!.EmailMfaEnabled, "Email MFA should be disabled after disable call");
    }

    #endregion

    #region Email OTP Code Flow

    [Fact(Skip = "May wait for rate limit cooldown - run manually")]
    public async Task EmailMfa_SendCode_StoresCodeAndQueuesEmail()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
        
        // Enable Email MFA first
        await _httpClient.PostAsync("/api/account/mfa/email/enable", null);

        // Wait to ensure clean state
        await Task.Delay(500);

        // Act - Send code (handle rate limit from previous test runs)
        var response = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var rateLimitResult = await response.Content.ReadFromJsonAsync<SendCodeResponse>();
            if (rateLimitResult?.RemainingSeconds > 0)
            {
                await Task.Delay((rateLimitResult.RemainingSeconds + 2) * 1000);
                response = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
            }
        }

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SendCodeResponse>();
        Assert.True(result!.Success, "Send code should succeed");
        Assert.Equal(60, result.RemainingSeconds); // 60-second cooldown

        // Cleanup
        await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
    }

    [Fact(Skip = "May wait for rate limit cooldown - run manually")]
    public async Task EmailMfa_VerifyCode_InvalidCode_ReturnsFalse()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
        
        // Enable Email MFA and send a code
        await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        
        // Send code with rate limit handling
        var sendResponse = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
        if (sendResponse.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var rateLimitResult = await sendResponse.Content.ReadFromJsonAsync<SendCodeResponse>();
            if (rateLimitResult?.RemainingSeconds > 0)
            {
                await Task.Delay((rateLimitResult.RemainingSeconds + 2) * 1000);
                await _httpClient.PostAsync("/api/account/mfa/email/send", null);
            }
        }

        // Act - Verify with invalid code
        var response = await _httpClient.PostAsJsonAsync("/api/account/mfa/email/verify", new { Code = "000000" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<VerifyCodeResponse>();
        Assert.False(result!.Success, "Verification with invalid code should fail");

        // Cleanup
        await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
    }

    [Fact(Skip = "Email MFA tests - run separately")]
    public async Task EmailMfa_VerifyCode_NoCodeSent_ReturnsFalse()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act - Try to verify without sending a code first
        var response = await _httpClient.PostAsJsonAsync("/api/account/mfa/email/verify", new { Code = "123456" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<VerifyCodeResponse>();
        Assert.False(result!.Success, "Verification without pending code should fail");
    }

    #endregion

    #region Authentication Requirement

    [Fact(Skip = "Email MFA tests - run separately")]
    public async Task EmailMfa_Endpoints_RequireAuthentication()
    {
        // Arrange - No auth header
        _httpClient.DefaultRequestHeaders.Authorization = null;

        // Act & Assert - All endpoints should return 401
        var enableResponse = await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        Assert.Equal(HttpStatusCode.Unauthorized, enableResponse.StatusCode);

        var disableResponse = await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
        Assert.Equal(HttpStatusCode.Unauthorized, disableResponse.StatusCode);

        var sendResponse = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
        Assert.Equal(HttpStatusCode.Unauthorized, sendResponse.StatusCode);

        var verifyResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/email/verify", new { Code = "123456" });
        Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
    }

    #endregion

    #region Concurrent MFA Methods

    [Fact(Skip = "Email MFA tests - run separately")]
    public async Task EmailMfa_CanCoexistWithTotpMfa()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // 1. Enable Email MFA
        await _httpClient.PostAsync("/api/account/mfa/email/enable", null);

        // 2. Also enable TOTP MFA (setup first)
        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        var setup = await setupResponse.Content.ReadFromJsonAsync<MfaSetupDto>();
        
        // Generate valid TOTP
        var totpCode = GenerateTotp(setup!.SharedKey);
        await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { Code = totpCode });

        // 3. Check both are enabled
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        
        Assert.True(status!.EmailMfaEnabled, "Email MFA should be enabled");
        Assert.True(status.TwoFactorEnabled, "TOTP MFA should also be enabled");

        // Cleanup
        await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
        await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = TEST_USER_PASSWORD });
    }

    #endregion

    #region Helper Methods

    private string GenerateTotp(string secretKey)
    {
        var bytes = OtpNet.Base32Encoding.ToBytes(secretKey.Replace(" ", ""));
        var totp = new OtpNet.Totp(bytes);
        return totp.ComputeTotp();
    }

    private async Task<string> GetUserTokenAsync(string username, string password)
    {
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

    private record SendCodeResponse
    {
        public bool Success { get; init; }
        public int RemainingSeconds { get; init; }
    }

    private record VerifyCodeResponse
    {
        public bool Success { get; init; }
    }

    #endregion
}
