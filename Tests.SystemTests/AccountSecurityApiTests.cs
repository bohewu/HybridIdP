using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for AccountSecurityController (/api/account/security-policy).
/// </summary>
public class AccountSecurityApiTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private string? _userToken;
    
    private const string TEST_USER_EMAIL = "admin@hybridauth.local";
    private const string TEST_USER_PASSWORD = "Admin@123";

    public AccountSecurityApiTests(WebIdPServerFixture serverFixture)
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
        _userToken = await GetUserTokenAsync(TEST_USER_EMAIL, TEST_USER_PASSWORD);
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetSecurityPolicy_AuthenticatedUser_ReturnsPolicy()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.GetAsync("/api/account/security-policy");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var policy = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        // Verify required fields exist
        Assert.True(policy.TryGetProperty("requireMfaForPasskey", out var requireMfaForPasskey));
        Assert.True(policy.TryGetProperty("enablePasskey", out _));
        Assert.True(policy.TryGetProperty("enableTotpMfa", out _));
        Assert.True(policy.TryGetProperty("enableEmailMfa", out _));
        Assert.True(policy.TryGetProperty("allowSelfPasswordChange", out _));
        
        // requireMfaForPasskey should be a boolean
        Assert.True(requireMfaForPasskey.ValueKind == JsonValueKind.True || 
                    requireMfaForPasskey.ValueKind == JsonValueKind.False);
    }

    [Fact]
    public async Task GetSecurityPolicy_Unauthenticated_Returns401()
    {
        // Arrange - no auth header

        // Act
        var response = await _httpClient.GetAsync("/api/account/security-policy");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }
}
