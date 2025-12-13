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

    // ===== OIDC Endpoints Coverage =====
    
    [Fact]
    public async Task Connect_Userinfo_WithoutAuth_Returns401()
    {
        // Act
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Connect_Introspection_WithoutAuth_Returns401Or404()
    {
        // Act
        var response = await _httpClient.PostAsync("/connect/introspection", new FormUrlEncodedContent(new Dictionary<string, string>()));

        // Assert - May return 401 or 404 depending on implementation
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Connect_Revocation_WithoutClientId_Returns4xx()
    {
        // Act
        var response = await _httpClient.PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string, string>()));

        // Assert - Should return 4xx (400/401/404)
        Assert.True((int)response.StatusCode >= 400 && (int)response.StatusCode < 500);
    }

    [Fact]
    public async Task Connect_Device_WithoutClientId_Returns4xx()
    {
        // Act
        var response = await _httpClient.PostAsync("/connect/device", new FormUrlEncodedContent(new Dictionary<string, string>()));

        // Assert - Should return 4xx
        Assert.True((int)response.StatusCode >= 400 && (int)response.StatusCode < 500);
    }

    [Fact]
    public async Task Connect_Logout_Post_Returns4xx()
    {
        // Act
        var response = await _httpClient.PostAsync("/connect/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        // Assert - Should return 4xx
        Assert.True((int)response.StatusCode >= 400 && (int)response.StatusCode < 500);
    }

    // ===== Admin API Endpoints Coverage (GET - Read-only, require auth) =====
    
    [Fact]
    public async Task AdminApi_Roles_WithoutAuth_Returns401()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/roles");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_Clients_WithoutAuth_Returns401()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/clients");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_Scopes_WithoutAuth_Returns401()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/scopes");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_Settings_WithoutAuth_Returns401()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/settings");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_People_WithoutAuth_Returns401()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/people");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_Audit_WithoutAuth_Returns4xx()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/audit");

        // Assert - May be 401 or 404
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminApi_Dashboard_WithoutAuth_Returns4xx()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/dashboard");

        // Assert - May be 401 or 404
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminApi_Resources_WithoutAuth_Returns401()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/resources");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_Permissions_WithoutAuth_Returns4xx()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/permissions");

        // Assert - May be 401 or 404
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminApi_Localization_WithoutAuth_Returns4xx()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/localization");

        // Assert - May be 401 or 404
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminApi_Claims_WithoutAuth_Returns4xx()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/claims");

        // Assert - May be 401 or 404
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminApi_Monitoring_WithoutAuth_Returns4xx()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/monitoring");

        // Assert - May be 401 or 404
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.NotFound);
    }

    // ===== Account Endpoints Coverage =====
    
    [Fact]
    public async Task Account_Login_Get_ReturnsOk()
    {
        // Act
        var response = await _httpClient.GetAsync("/Account/Login");

        // Assert - Should return OK or Redirect
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Account_Logout_Get_ReturnsOk()
    {
        // Act
        var response = await _httpClient.GetAsync("/Account/Logout");

        // Assert
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect);
    }
    
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
