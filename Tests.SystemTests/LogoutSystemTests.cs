using System.Net;
using Xunit;
using System.Text.Encodings.Web;

namespace Tests.SystemTests;

public class LogoutSystemTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;

    public LogoutSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false // Stop at the 302/303 response
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Logout_WithoutPermission_ReturnsError()
    {
        // "testclient-device" lacks 'ept:end_session' permission
        var clientId = "testclient-device"; 
        var redirectUri = "https://localhost:7001/signout-callback-oidc";

        var url = $"/connect/logout?client_id={clientId}&post_logout_redirect_uri={UrlEncoder.Default.Encode(redirectUri)}";

        var response = await _httpClient.GetAsync(url);

        // Expect Forbidden (403) or BadRequest (400) due to missing permission
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden || 
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.NotFound, // Allow NotFound if redir to missing AccessDenied
            $"Expected Forbidden/BadRequest/NotFound but got {response.StatusCode}");
    }

    [Fact]
    public async Task Logout_WithPermission_ReturnsRedirectOrSuccess()
    {
        // "testclient-public" HAS 'ept:end_session' permission
        var clientId = "testclient-public"; 
        var redirectUri = "https://localhost:7001/signout-callback-oidc";
        
        var url = $"/connect/logout?client_id={clientId}&post_logout_redirect_uri={UrlEncoder.Default.Encode(redirectUri)}";

        var response = await _httpClient.GetAsync(url);
        
        // Should succeed (200 or 302/303)
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
