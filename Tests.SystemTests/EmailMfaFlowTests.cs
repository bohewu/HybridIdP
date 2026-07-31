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
[Collection("Shared Server")]
public class EmailMfaFlowTests : IAsyncLifetime
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
        await Task.Delay(100);
        
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

    [Fact]
    public async Task EmailMfa_DirectEnableWithoutProof_DoesNotChangeState()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // 1. Check initial status - should be disabled
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(status!.EmailMfaEnabled, "Email MFA should be disabled initially");

        // 2. Direct enable without an OTP possession proof must fail
        var enableResponse = await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        Assert.Equal(HttpStatusCode.BadRequest, enableResponse.StatusCode);

        // 3. Verify it remains disabled
        var statusAfterEnable = await _httpClient.GetAsync("/api/account/mfa/status");
        var enabledStatus = await statusAfterEnable.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(enabledStatus!.EmailMfaEnabled, "Email MFA must remain disabled without proof");
    }

    #endregion

    #region Email OTP Code Flow

    // NOTE: Rate-limit dependent tests removed to avoid flaky CI builds.
    // Core functionality is tested in MfaApiTests.EmailMfa_* tests.

    [Fact]
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

        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(status!.EmailMfaEnabled, "Failed verification must not enable Email MFA");
    }

    #endregion

    #region Authentication Requirement

    [Fact]
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

    [Fact]
    public async Task EmailMfa_CannotBeAddedToTotpSessionWithoutEmailProof()
    {
        await MfaEnrollmentTestClient.AuthorizeAsync(
            _httpClient,
            TEST_USER_EMAIL,
            TEST_USER_PASSWORD);

        // 1. Enable TOTP MFA after fresh interactive reauthentication.
        var setupResponse =
            await _httpClient.GetAsync("/api/account/mfa-setup/totp/setup");
        var setup = await setupResponse.Content.ReadFromJsonAsync<MfaSetupDto>();
        
        // Generate valid TOTP
        var totpCode = GenerateTotp(setup!.SharedKey);
        await _httpClient.PostAsJsonAsync(
            "/api/account/mfa-setup/totp/verify",
            new { Code = totpCode });

        // 2. Direct Email MFA enablement still requires its own possession proof.
        var directEnableResponse =
            await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        Assert.Equal(HttpStatusCode.BadRequest, directEnableResponse.StatusCode);

        // 3. TOTP remains enabled, but Email MFA does not.
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        
        Assert.False(status!.EmailMfaEnabled, "Email MFA requires its own verified code");
        Assert.True(status.TwoFactorEnabled, "TOTP MFA should also be enabled");

        // Cleanup
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
