using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

/// <summary>
/// Phase 13 OAuth Flow Integration Tests
/// Tests Client Credentials, Introspection, and Revocation flows at the HTTP level
/// </summary>
public class OAuthFlowIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private const string M2M_CLIENT_ID = "testclient-m2m";
    private const string M2M_CLIENT_SECRET = "m2m-test-secret-2024";

    public OAuthFlowIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ClientCredentials_ValidRequest_ReturnsAccessToken()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", M2M_CLIENT_ID),
            new KeyValuePair<string, string>("client_secret", M2M_CLIENT_SECRET),
            new KeyValuePair<string, string>("scope", "api:company:read api:company:write")
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(content);
        
        Assert.True(tokenResponse.TryGetProperty("access_token", out var accessToken));
        Assert.NotNull(accessToken.GetString());
        Assert.True(tokenResponse.TryGetProperty("token_type", out var tokenType));
        Assert.Equal("Bearer", tokenResponse.GetProperty("token_type").GetString());
    }

    [Fact]
    public async Task ClientCredentials_InvalidSecret_ReturnsUnauthorized()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", M2M_CLIENT_ID),
            new KeyValuePair<string, string>("client_secret", "wrong-secret"),
            new KeyValuePair<string, string>("scope", "api:company:read")
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TokenIntrospection_ValidToken_ReturnsActive()
    {
        // Arrange - Get a valid token first
        var tokenRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", M2M_CLIENT_ID),
            new KeyValuePair<string, string>("client_secret", M2M_CLIENT_SECRET),
            new KeyValuePair<string, string>("scope", "api:company:read")
        });

        var tokenResponse = await _client.PostAsync("/connect/token", tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenContent);
        var accessToken = tokenData.GetProperty("access_token").GetString();

        // Act - Introspect the token
        var introspectRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("token", accessToken!),
            new KeyValuePair<string, string>("client_id", M2M_CLIENT_ID),
            new KeyValuePair<string, string>("client_secret", M2M_CLIENT_SECRET)
        });

        var introspectResponse = await _client.PostAsync("/connect/introspect", introspectRequest);

        // Assert
        introspectResponse.EnsureSuccessStatusCode();
        var introspectContent = await introspectResponse.Content.ReadAsStringAsync();
        var introspectData = JsonSerializer.Deserialize<JsonElement>(introspectContent);
        
        Assert.True(introspectData.TryGetProperty("active", out var active));
        Assert.True(active.GetBoolean());
    }

    [Fact]
    public async Task TokenRevocation_ValidToken_SuccessfullyRevokes()
    {
        // Arrange - Get a valid token first
        var tokenRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", M2M_CLIENT_ID),
            new KeyValuePair<string, string>("client_secret", M2M_CLIENT_SECRET),
            new KeyValuePair<string, string>("scope", "api:company:read")
        });

        var tokenResponse = await _client.PostAsync("/connect/token", tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenContent);
        var accessToken = tokenData.GetProperty("access_token").GetString();

        // Act - Revoke the token
        var revokeRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("token", accessToken!),
            new KeyValuePair<string, string>("client_id", M2M_CLIENT_ID),
            new KeyValuePair<string, string>("client_secret", M2M_CLIENT_SECRET)
        });

        var revokeResponse = await _client.PostAsync("/connect/revoke", revokeRequest);

        // Assert - Revocation should succeed
        revokeResponse.EnsureSuccessStatusCode();

        // Verify token is no longer active via introspection
        var verifyRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("token", accessToken!),
            new KeyValuePair<string, string>("client_id", M2M_CLIENT_ID),
            new KeyValuePair<string, string>("client_secret", M2M_CLIENT_SECRET)
        });

        var verifyResponse = await _client.PostAsync("/connect/introspect", verifyRequest);
        verifyResponse.EnsureSuccessStatusCode();
        
        var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
        var verifyData = JsonSerializer.Deserialize<JsonElement>(verifyContent);
        
        Assert.True(verifyData.TryGetProperty("active", out var active));
        Assert.False(active.GetBoolean());
    }

    [Fact]
    public async Task ClientCredentials_PublicScopes_ShouldBeBlockedByClient()
    {
        // Arrange - Try to request OIDC scopes with M2M client
        // Note: This test assumes the M2M client was NOT registered with public scopes
        var request = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", M2M_CLIENT_ID),
            new KeyValuePair<string, string>("client_secret", M2M_CLIENT_SECRET),
            new KeyValuePair<string, string>("scope", "openid profile email api:company:read")
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert
        // Should fail because M2M client doesn't have permission for OIDC scopes
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var errorData = JsonSerializer.Deserialize<JsonElement>(content);
        
        Assert.True(errorData.TryGetProperty("error", out var error));
        Assert.Equal("invalid_scope", error.GetString());
    }
}
