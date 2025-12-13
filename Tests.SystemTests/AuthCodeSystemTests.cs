using System.Net;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for Authorization Code Flow
/// Tests against running Web.IdP server
/// </summary>
[Collection("SystemTests")]
public class AuthCodeSystemTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;

    public AuthCodeSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    public async Task InitializeAsync()
    {
        // Ensure server is running before tests
        await _serverFixture.EnsureServerRunningAsync();
        
        // Wait for full initialization
        await Task.Delay(2000);
    }

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Server_IsRunning_AndRespondsToHealthCheck()
    {
        // Act
        var response = await _httpClient.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task AuthorizeEndpoint_WithoutParameters_ReturnsBadRequest()
    {
        // Act - Try to access authorize endpoint without required parameters
        var response = await _httpClient.GetAsync("/connect/authorize");

        // Assert - OpenIddict should return BadRequest for missing required parameters
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    
    [Fact]
    public async Task TokenEndpoint_WithoutGrant_ReturnsBadRequest()
    {
        // Act - Try to get token without grant
        var response = await _httpClient.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>()));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // NOTE: Full authorization code flow testing is better suited for Integration Tests
    // using WebApplicationFactory, as it requires:
    // 1. Login form interaction (username/password submission)
    // 2. Cookie/session management
    // 3. Consent page handling
    // 4. CSRF token extraction
    // These are more reliably tested with in-memory test server rather than external HTTP calls
}
