using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for MFA API endpoints.
/// Uses seeded testuser@hybridauth.local with password flow (testclient-public).
/// </summary>
public class MfaApiTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
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
        await Task.Delay(1000);
        
        // Get token for seeded test user using password flow with testclient-public
        _userToken = await GetUserTokenAsync(TEST_USER_EMAIL, TEST_USER_PASSWORD);
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
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

    #region Helper Methods

    private async Task<string> GetUserTokenAsync(string username, string password)
    {
        // Use testclient-public which has password flow enabled
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

    #endregion

    #region DTOs

    private record MfaStatusDto
    {
        public bool TwoFactorEnabled { get; init; }
        public bool HasAuthenticator { get; init; }
        public int RecoveryCodesLeft { get; init; }
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
