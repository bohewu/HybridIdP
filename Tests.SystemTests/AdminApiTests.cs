using System.Net;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Admin API endpoint tests
/// Uses WebIdPServerFixture to auto-manage server lifecycle
/// 
/// NOTE: Current tests are READ-ONLY (validation of 401/400 responses)
/// Future CRUD tests MUST cleanup test data in DisposeAsync()
/// See Tests.SystemTests/TEST_DATA_CLEANUP.md for guidelines
/// </summary>
public class AdminApiTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;

    public AdminApiTests(WebIdPServerFixture serverFixture)
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
    }

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AdminApi_WithoutAuth_Returns401()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/users");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenEndpoint_WithoutGrantType_ReturnsBadRequest()
    {
        // Arrange
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "test"
        });

        // Act
        var response = await _httpClient.PostAsync("/connect/token", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizeEndpoint_WithoutParams_ReturnsBadRequest()
    {
        // Act
        var response = await _httpClient.GetAsync("/connect/authorize");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task M2M_GetToken_WithValidClientCredentials_ReturnsAccessToken()
    {
        // Arrange - Use seeded M2M client from ClientSeeder
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-m2m",
            ["client_secret"] = "m2m-test-secret-2024", 
            ["scope"] = "api:company:read api:company:write"
        });

        // Act
        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("access_token", content);
        Assert.Contains("token_type", content);
    }

    // NOTE: Admin API requires specific scopes that testclient-m2m doesn't have
    // Skipping authenticated Admin API tests for now - require dedicated admin client
    // See TEST_DATA_CLEANUP.md for future CRUD test patterns
    
    // Helper to get M2M access token
    private async Task<string> GetM2MTokenAsync()
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-m2m",
            ["client_secret"] = "m2m-test-secret-2024",
            ["scope"] = "api:company:read api:company:write"
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = System.Text.Json.JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }
}
