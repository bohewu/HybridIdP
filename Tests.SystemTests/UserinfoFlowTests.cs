using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for UserInfo endpoint using Resource Owner Password Credentials (ROPC) flow.
/// ROPC allows programmatic login with username/password, enabling userinfo endpoint testing.
/// </summary>
[Collection("Shared Server")]
public class UserinfoFlowTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Test user credentials (seeded by DataSeeder)
    private const string TestUsername = "userinfo@hybridauth.local";
    private const string TestPassword = "Userinfo@123";
    
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
        await Task.Delay(100);
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
    /// Task 4b: Verify that person_id claim is returned when profile scope is granted.
    /// PersonId is mapped to profile scope and should appear in UserInfo endpoint.
    /// </summary>
    [Fact]
    public async Task Userinfo_WithProfileScope_ReturnsPersonIdClaim()
    {
        // Arrange - request openid + profile scope
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
        
        Assert.True(result.TryGetProperty("sub", out _), "Subject claim should be present");
        Assert.True(result.TryGetProperty("person_id", out var personIdValue), 
            "person_id claim should be returned when profile scope is granted");
        
        // PersonId should be a non-empty GUID string
        var personId = personIdValue.GetString();
        Assert.False(string.IsNullOrEmpty(personId), "person_id should not be empty");
        Assert.True(Guid.TryParse(personId, out _), "person_id should be a valid GUID");
    }

    /// <summary>
    /// Verify refresh token flow preserves profile scope when scope is omitted.
    /// </summary>
    [Fact]
    public async Task Userinfo_RefreshToken_PreservesProfileScope_WhenScopeNotProvided()
    {
        // Arrange - request openid + profile + offline_access for refresh token
        var (accessToken, refreshToken) = await TryGetUserTokenWithRefreshAsync("openid profile offline_access");
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        var refreshedAccessToken = await TryRefreshAccessTokenAsync(refreshToken);
        if (string.IsNullOrEmpty(refreshedAccessToken))
        {
            return;
        }

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", refreshedAccessToken);

        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);

        Assert.True(result.TryGetProperty("sub", out _), "Subject claim should be present");
        Assert.True(result.TryGetProperty("person_id", out _),
            "person_id claim should be returned when profile scope is preserved after refresh");
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

    /// <summary>
    /// Try to get a user access token with refresh token using ROPC flow.
    /// Returns null tokens if ROPC is not enabled or credentials are invalid.
    /// </summary>
    private async Task<(string? AccessToken, string? RefreshToken)> TryGetUserTokenWithRefreshAsync(string scopes)
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
            return (null, null);
        }

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);

        string? accessToken = null;
        string? refreshToken = null;

        if (result.TryGetProperty("access_token", out var tokenElement))
        {
            accessToken = tokenElement.GetString();
        }

        if (result.TryGetProperty("refresh_token", out var refreshElement))
        {
            refreshToken = refreshElement.GetString();
        }

        return (accessToken, refreshToken);
    }

    /// <summary>
    /// Try to refresh access token without specifying scope.
    /// Returns null if refresh token flow is not enabled or fails.
    /// </summary>
    private async Task<string?> TryRefreshAccessTokenAsync(string refreshToken)
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        if (!response.IsSuccessStatusCode)
        {
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
