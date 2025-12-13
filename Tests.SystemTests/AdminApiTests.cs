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
    
    // TODO: Add M2M authenticated tests
}
