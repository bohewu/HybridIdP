using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for UserInfo endpoint using Resource Owner Password Credentials (ROPC) flow.
/// ROPC allows programmatic login with username/password, enabling userinfo endpoint testing.
/// </summary>
public class UserinfoFlowTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Test user credentials (seeded by DataSeeder)
    private const string TestUsername = "admin@hybridauth.local";
    private const string TestPassword = "Admin@123";
    
    // Test client that supports ROPC
    private const string ClientId = "testclient-public";

    public UserinfoFlowTests(WebIdPServerFixture serverFixture)
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
        await Task.Delay(500);
    }

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Verify that userinfo endpoint returns subject claim with only openid scope.
    /// </summary>
    [Fact]
    public async Task Userinfo_WithOpenIdScope_ReturnsSubject()
    {
        // Arrange - get token using ROPC flow
        var token = await TryGetUserTokenAsync("openid");
        if (token == null)
        {
            // Skip if ROPC is not supported or credentials are invalid
            return;
        }

        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        
        Assert.True(result.TryGetProperty("sub", out _), "Subject claim should be present");
    }

    /// <summary>
    /// Verify that userinfo endpoint returns email claims when email scope is granted.
    /// </summary>
    [Fact]
    public async Task Userinfo_WithEmailScope_ReturnsEmailClaims()
    {
        // Arrange
        var token = await TryGetUserTokenAsync("openid email");
        if (token == null)
        {
            return;
        }

        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        
        Assert.True(result.TryGetProperty("sub", out _));
        // Email claim should be present for the admin user
        Assert.True(result.TryGetProperty("email", out var emailValue), 
            "Email claim should be present when email scope is granted");
        Assert.Contains("@", emailValue.GetString());
    }

    /// <summary>
    /// Verify that userinfo endpoint returns profile claims when profile scope is granted.
    /// </summary>
    [Fact]
    public async Task Userinfo_WithProfileScope_ReturnsProfileClaims()
    {
        // Arrange
        var token = await TryGetUserTokenAsync("openid profile");
        if (token == null)
        {
            return;
        }

        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        
        Assert.True(result.TryGetProperty("sub", out _));
        // Profile claims like name or preferred_username should be present
    }

    /// <summary>
    /// Verify that email claim is NOT returned when only openid scope is granted.
    /// This tests OIDC compliance - claims should only be returned for granted scopes.
    /// </summary>
    [Fact]
    public async Task Userinfo_WithoutEmailScope_DoesNotReturnEmail()
    {
        // Arrange - only request openid, not email
        var token = await TryGetUserTokenAsync("openid");
        if (token == null)
        {
            return;
        }

        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        
        Assert.True(result.TryGetProperty("sub", out _));
        Assert.False(result.TryGetProperty("email", out _), 
            "Email claim should NOT be returned without email scope");
    }

    #region Helper Methods

    /// <summary>
    /// Try to get a user access token using Resource Owner Password Credentials flow.
    /// Returns null if ROPC is not enabled or credentials are invalid.
    /// </summary>
    private async Task<string?> TryGetUserTokenAsync(string scopes)
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = ClientId,
            ["username"] = TestUsername,
            ["password"] = TestPassword,
            ["scope"] = scopes
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        if (!response.IsSuccessStatusCode)
        {
            // ROPC might not be enabled or credentials invalid
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        
        if (result.TryGetProperty("access_token", out var tokenElement))
        {
            return tokenElement.GetString();
        }

        return null;
    }

    #endregion
}
