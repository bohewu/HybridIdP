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
}
