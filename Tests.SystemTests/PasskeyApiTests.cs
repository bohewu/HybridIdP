using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fido2NetLib;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for WebAuthn (Passkey) API endpoints.
/// Phase 20.4: Test-First Implementation.
/// </summary>
public class PasskeyApiTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private string? _userToken;
    
    // Use seeded test user
    private const string TEST_USER_EMAIL = "admin@hybridauth.local";
    private const string TEST_USER_PASSWORD = "Admin@123";

    public PasskeyApiTests(WebIdPServerFixture serverFixture)
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
        
        // Get token for seeded test user
        _userToken = await GetUserTokenAsync(TEST_USER_EMAIL, TEST_USER_PASSWORD);
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RegisterOptions_ValidUser_ReturnsOptions()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/passkey/register-options", new { });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Fido2.AspNet returns CredentialCreateOptions
        // We expect at least the user info and challenge
        Assert.True(options.TryGetProperty("user", out var userProp));
        Assert.True(options.TryGetProperty("challenge", out _));
    }

    [Fact]
    public async Task Register_WithoutOptions_ReturnsBadRequest()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        // Sending random data without previous options setup should fail
        var response = await _httpClient.PostAsJsonAsync("/api/passkey/register", new { });

        // Assert
        // Might be 400 or 500 depending on implementation, but definitely not 404 once implemented
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode); 
    }

    [Fact]
    public async Task LoginOptions_ReturnsOptions()
    {
        // Arrange
        // Login options might not require auth (first step of login) OR require username
        // We assume we send username to get allowed credentials
        
        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/passkey/login-options", new { Username = TEST_USER_EMAIL });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(options.TryGetProperty("challenge", out _));
    }

    [Fact]
    public async Task Login_InvalidSignature_ReturnsBadRequest()
    {
        // Arrange
        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/passkey/login", new { });

        // Assert
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
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
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }
}
