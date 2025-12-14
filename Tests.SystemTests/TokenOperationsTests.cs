using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Tests for Introspection and Revocation OIDC endpoints.
/// </summary>
public class TokenOperationsTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _accessToken;

    public TokenOperationsTests(WebIdPServerFixture serverFixture)
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
        _accessToken = await GetAccessTokenAsync();
    }

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }

    // ===== Introspection Tests =====

    [Fact]
    public async Task Introspect_ValidToken_ReturnsActive()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = _accessToken!,
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024"
        });

        // Act
        var response = await _httpClient.PostAsync("/connect/introspect", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("active", out var active));
        Assert.True(active.GetBoolean());
    }

    [Fact]
    public async Task Introspect_InvalidToken_ReturnsInactive()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "invalid_token_12345",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024"
        });

        // Act
        var response = await _httpClient.PostAsync("/connect/introspect", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("active", out var active));
        Assert.False(active.GetBoolean());
    }

    [Fact]
    public async Task Introspect_NoClientCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = _accessToken!
        });

        // Act
        var response = await _httpClient.PostAsync("/connect/introspect", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Revocation Tests =====

    [Fact]
    public async Task Revoke_ValidToken_ReturnsOk()
    {
        // Arrange - get a fresh token to revoke
        var tokenToRevoke = await GetAccessTokenAsync();
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = tokenToRevoke,
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024"
        });

        // Act
        var response = await _httpClient.PostAsync("/connect/revoke", request);

        // Assert - revocation always returns 200 per RFC 7009
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify token is now inactive
        var introspectRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = tokenToRevoke,
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024"
        });
        var introspectResponse = await _httpClient.PostAsync("/connect/introspect", introspectRequest);
        var content = await introspectResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("active", out var active));
        Assert.False(active.GetBoolean());
    }

    [Fact]
    public async Task Revoke_InvalidToken_ReturnsOk()
    {
        // Arrange - per RFC 7009, revocation of invalid tokens should succeed
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "invalid_token_xyz",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024"
        });

        // Act
        var response = await _httpClient.PostAsync("/connect/revoke", request);

        // Assert - should return 200 even for invalid tokens
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_NoClientCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = _accessToken!
        });

        // Act
        var response = await _httpClient.PostAsync("/connect/revoke", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Userinfo Tests =====

    [Fact]
    public async Task Userinfo_NoAuth_ReturnsUnauthorized()
    {
        // Arrange - no auth header
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Userinfo_InvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", "invalid_token_xyz");

        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Userinfo_ValidToken_ReturnsUserInfo()
    {
        // Arrange - need token with openid scope
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _accessToken);

        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("sub", out _));
    }

    [Fact]
    public async Task Userinfo_OnlyOpenIdScope_ReturnsOnlySubject()
    {
        // Arrange - get token with only openid scope (no email, no profile)
        var token = await GetAccessTokenWithScopesAsync("openid");
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        
        // Subject should always be present
        Assert.True(result.TryGetProperty("sub", out _));
        
        // Email should NOT be present (no email scope)
        Assert.False(result.TryGetProperty("email", out _), 
            "Email should not be returned without email scope");
        
        // Profile claims should NOT be present (no profile scope)
        Assert.False(result.TryGetProperty("name", out _), 
            "Name should not be returned without profile scope");
    }

    [Fact]
    public async Task Userinfo_WithEmailScope_CanAcquireToken()
    {
        // Arrange - verify client can acquire token with openid + email scope
        // Note: Full userinfo testing is done via unit tests; this verifies client config
        var token = await TryGetAccessTokenWithScopesAsync("openid email");
        
        // Assert - token acquisition should succeed now that client has email scope
        Assert.NotNull(token);
    }

    [Fact]
    public async Task Userinfo_WithProfileScope_CanAcquireToken()
    {
        // Arrange - verify client can acquire token with openid + profile scope
        // Note: Full userinfo testing is done via unit tests; this verifies client config
        var token = await TryGetAccessTokenWithScopesAsync("openid profile");
        
        // Assert - token acquisition should succeed now that client has profile scope
        Assert.NotNull(token);
    }

    [Fact]
    public async Task Userinfo_WithRolesScope_CanAcquireToken()
    {
        // Arrange - verify client can acquire token with openid + roles scope
        // Note: Full userinfo testing is done via unit tests; this verifies client config
        var token = await TryGetAccessTokenWithScopesAsync("openid roles");
        
        // Assert - token acquisition should succeed now that client has roles scope
        Assert.NotNull(token);
    }

    // ===== Helper Methods =====

    private async Task<string> GetAccessTokenAsync()
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024",
            ["scope"] = "openid"
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("access_token").GetString()!;
    }

    private async Task<string> GetAccessTokenWithScopesAsync(string scopes)
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024",
            ["scope"] = scopes
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// Try to get an access token with specified scopes.
    /// Returns null if the client doesn't have permission for the requested scopes.
    /// </summary>
    private async Task<string?> TryGetAccessTokenWithScopesAsync(string scopes)
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024",
            ["scope"] = scopes
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        if (!response.IsSuccessStatusCode)
        {
            return null; // Client doesn't have permission for these scopes
        }

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("access_token").GetString()!;
    }
}
