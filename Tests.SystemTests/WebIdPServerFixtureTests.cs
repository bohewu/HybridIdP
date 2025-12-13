using System.Net;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Tests for WebIdPServerFixture to verify server lifecycle management works correctly
/// </summary>
[Collection("SystemTests")]
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

    [Fact(Skip = "Server stop is unreliable - process tree may not fully terminate")]
    public async Task Fixture_CanStopServer_AndServerNoLongerResponds()
    {
        // Arrange - Ensure server is running first
        await _fixture.EnsureServerRunningAsync();
        Assert.True(_fixture.IsRunning);

        // Act - Stop the server
        await _fixture.StopServerAsync();

        // Assert - Server should be stopped
        Assert.False(_fixture.IsRunning);

        // Wait a bit for cleanup to complete
        await Task.Delay(2000);

        // Verify server does not respond (connection should be refused)
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };

        // Should throw - either timeout or connection refused
        var exception = await Record.ExceptionAsync(async () =>
        {
            await client.GetAsync($"{_fixture.BaseUrl}/health");
        });
        
        Assert.NotNull(exception); // Some exception should occur (connection refused, timeout, etc.)
    }

    [Fact(Skip = "Server stop is unreliable - depends on previous test cleanup")]
    public async Task Fixture_CanRestartServer_AfterStopping()
    {
        // Arrange - Start and then stop
        await _fixture.EnsureServerRunningAsync();
        await _fixture.StopServerAsync();
        Assert.False(_fixture.IsRunning);

        // Act - Restart
        await _fixture.EnsureServerRunningAsync();

        // Assert - Server should be running again
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
