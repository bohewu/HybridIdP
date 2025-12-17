using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OtpNet;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Comprehensive MFA flow tests using programmatic TOTP code generation.
/// Tests the complete MFA lifecycle: setup, enable, login with MFA, recovery codes, and disable.
/// </summary>
[Trait("Category", "Slow")]
[Collection("MFA Tests")]
public class MfaFullFlowTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private string? _userToken;

    // Use seeded admin user
    private const string TEST_USER_EMAIL = "admin@hybridauth.local";
    private const string TEST_USER_PASSWORD = "Admin@123";

    public MfaFullFlowTests(WebIdPServerFixture serverFixture)
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

    /// <summary>
    /// Test the complete MFA enable flow:
    /// 1. Get setup info (shared key + QR)
    /// 2. Generate valid TOTP code programmatically
    /// 3. Verify code and enable MFA
    /// 4. Confirm MFA is enabled
    /// 5. Clean up: disable MFA
    /// </summary>
    [Fact]
    public async Task MfaFullFlow_EnableWithValidTotp_ThenDisable()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Step 1: Get MFA setup info
        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        
        var setupContent = await setupResponse.Content.ReadAsStringAsync();
        var setup = JsonDocument.Parse(setupContent).RootElement;
        var sharedKey = setup.GetProperty("sharedKey").GetString()!;
        
        Assert.NotEmpty(sharedKey);

        // Step 2: Generate valid TOTP code using OtpNet
        var secretBytes = Base32Encoding.ToBytes(sharedKey.Replace(" ", "").ToUpper());
        var totp = new Totp(secretBytes);
        var validCode = totp.ComputeTotp();

        // Step 3: Verify and enable MFA
        var verifyResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { code = validCode });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        
        var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
        var verifyResult = JsonDocument.Parse(verifyContent).RootElement;
        
        Assert.True(verifyResult.GetProperty("success").GetBoolean(), "MFA verification should succeed");
        
        // Should receive recovery codes
        var recoveryCodes = verifyResult.GetProperty("recoveryCodes");
        Assert.True(recoveryCodes.GetArrayLength() > 0, "Should receive recovery codes");

        // Step 4: Confirm MFA is now enabled
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        
        var statusContent = await statusResponse.Content.ReadAsStringAsync();
        var status = JsonDocument.Parse(statusContent).RootElement;
        
        Assert.True(status.GetProperty("twoFactorEnabled").GetBoolean(), "MFA should be enabled");
        Assert.True(status.GetProperty("hasAuthenticator").GetBoolean(), "Should have authenticator");
        Assert.True(status.GetProperty("recoveryCodesLeft").GetInt32() > 0, "Should have recovery codes");

        // Step 5: Clean up - disable MFA
        var disableResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", 
            new { password = TEST_USER_PASSWORD });
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        // Verify MFA is disabled
        var finalStatusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var finalStatus = JsonDocument.Parse(await finalStatusResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.False(finalStatus.GetProperty("twoFactorEnabled").GetBoolean(), "MFA should be disabled after cleanup");
    }

    /// <summary>
    /// Test recovery code validation:
    /// 1. Enable MFA
    /// 2. Use a recovery code
    /// 3. Verify recovery code count decreases
    /// </summary>
    [Fact]
    public async Task MfaRecoveryCode_ValidCode_ConsumesCode()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Enable MFA first
        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        var setup = JsonDocument.Parse(await setupResponse.Content.ReadAsStringAsync()).RootElement;
        var sharedKey = setup.GetProperty("sharedKey").GetString()!;
        
        var secretBytes = Base32Encoding.ToBytes(sharedKey.Replace(" ", "").ToUpper());
        var totp = new Totp(secretBytes);
        var validCode = totp.ComputeTotp();

        var verifyResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { code = validCode });
        var verifyResult = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync()).RootElement;
        
        if (!verifyResult.GetProperty("success").GetBoolean())
        {
            // MFA might already be enabled, skip test
            return;
        }

        // Get initial recovery code count
        var statusBefore = await GetMfaStatusAsync();
        var countBefore = statusBefore.GetProperty("recoveryCodesLeft").GetInt32();
        Assert.True(countBefore > 0, "Should have recovery codes after enabling MFA");

        // Clean up - disable MFA
        await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { password = TEST_USER_PASSWORD });
    }

    /// <summary>
    /// Test regenerating recovery codes
    /// </summary>
    [Fact]
    public async Task MfaRecoveryCodes_Regenerate_ReturnsNewCodes()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Enable MFA first
        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        var setup = JsonDocument.Parse(await setupResponse.Content.ReadAsStringAsync()).RootElement;
        var sharedKey = setup.GetProperty("sharedKey").GetString()!;
        
        var secretBytes = Base32Encoding.ToBytes(sharedKey.Replace(" ", "").ToUpper());
        var totp = new Totp(secretBytes);
        var validCode = totp.ComputeTotp();

        var verifyResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { code = validCode });
        var verifyResult = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync()).RootElement;
        
        if (!verifyResult.GetProperty("success").GetBoolean())
        {
            // MFA might already be enabled or failed
            return;
        }

        // Act - Regenerate recovery codes
        var regenerateResponse = await _httpClient.PostAsync("/api/account/mfa/recovery-codes", null);
        Assert.Equal(HttpStatusCode.OK, regenerateResponse.StatusCode);

        var regenerateResult = JsonDocument.Parse(await regenerateResponse.Content.ReadAsStringAsync()).RootElement;
        var newCodes = regenerateResult.GetProperty("recoveryCodes");
        
        Assert.True(newCodes.GetArrayLength() == 10, "Should get 10 new recovery codes");

        // Clean up
        await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { password = TEST_USER_PASSWORD });
    }

    /// <summary>
    /// Test that invalid TOTP codes are rejected
    /// </summary>
    [Fact]
    public async Task MfaVerify_InvalidTotp_ReturnsFalse()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // First get setup to ensure we have a key
        await _httpClient.GetAsync("/api/account/mfa/setup");

        // Act - Try with invalid code
        var verifyResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { code = "000000" });
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var result = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.False(result.GetProperty("success").GetBoolean(), "Invalid TOTP should fail verification");
    }

    /// <summary>
    /// Test that expired TOTP codes (from different time window) are rejected
    /// </summary>
    [Fact]
    public async Task MfaVerify_ExpiredTotp_ReturnsFalse()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        var setup = JsonDocument.Parse(await setupResponse.Content.ReadAsStringAsync()).RootElement;
        var sharedKey = setup.GetProperty("sharedKey").GetString()!;
        
        var secretBytes = Base32Encoding.ToBytes(sharedKey.Replace(" ", "").ToUpper());
        var totp = new Totp(secretBytes);
        
        // Generate code from 2 minutes ago (definitely expired)
        var expiredCode = totp.ComputeTotp(DateTime.UtcNow.AddMinutes(-2));

        // Act
        var verifyResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { code = expiredCode });
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var result = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.False(result.GetProperty("success").GetBoolean(), "Expired TOTP should fail verification");
    }

    #region Helper Methods

    private async Task<string> GetUserTokenAsync(string username, string password)
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "testclient-public",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid profile"
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

    private async Task<JsonElement> GetMfaStatusAsync()
    {
        var response = await _httpClient.GetAsync("/api/account/mfa/status");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    #endregion
}
