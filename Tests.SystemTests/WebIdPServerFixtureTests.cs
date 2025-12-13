using System.Net;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Tests for WebIdPServerFixture to verify server lifecycle management works correctly
/// </summary>
public class WebIdPServerFixtureTests : IClassFixture<WebIdPServerFixture>
{
    private readonly WebIdPServerFixture _fixture;

    public WebIdPServerFixtureTests(WebIdPServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fixture_CanStartServer_AndServerRespondsToRequests()
    {
        // Arrange & Act
        await _fixture.EnsureServerRunningAsync();

        // Assert
        Assert.True(_fixture.IsRunning, "Server should be marked as running");

        // Verify server actually responds
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler);
        
        var response = await client.GetAsync($"{_fixture.BaseUrl}/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Fixture_WhenCalledMultipleTimes_DoesNotStartMultipleServers()
    {
        // Arrange & Act - Call multiple times
        await _fixture.EnsureServerRunningAsync();
        await _fixture.EnsureServerRunningAsync();
        await _fixture.EnsureServerRunningAsync();

        // Assert - Server should still be running (not crashed from multiple starts)
        Assert.True(_fixture.IsRunning);
        
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler);
        
        var response = await client.GetAsync($"{_fixture.BaseUrl}/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
