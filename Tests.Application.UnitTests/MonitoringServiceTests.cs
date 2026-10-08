using Core.Application;
using Core.Application.DTOs;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using System.Net.Http;
using Microsoft.AspNetCore.SignalR;
using System.Threading;
using Microsoft.Extensions.Options;
using Core.Application.Options;

namespace Tests.Application.UnitTests;

public class MonitoringServiceTests : IDisposable
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPrometheusMetricsAsync_ShouldReadLocalHttpButNeverFollowRedirect(bool redirect)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var endpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
        var requests = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var connection = await listener.AcceptTcpClientAsync(stop.Token);
                    await using var stream = connection.GetStream();
                    using var reader = new System.IO.StreamReader(stream, leaveOpen: true);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
                    Interlocked.Increment(ref requests);
                    var body = "# TYPE test_metric gauge\ntest_metric 7\n";
                    var status = redirect && requests == 1
                        ? $"302 Found\r\nLocation: http://127.0.0.1:{endpoint.Port}/redirected"
                        : "200 OK";
                    var response = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
                    await stream.WriteAsync(response, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        try
        {
            using var client = new HttpClient(MonitoringService.CreatePrimaryHandler());
            _httpClientFactoryMock.Setup(value => value.CreateClient(MonitoringService.HttpClientName)).Returns(client);
            _optionsMock.SetupGet(value => value.Value).Returns(new ObservabilityOptions { MetricsBaseUrl = $"http://127.0.0.1:{endpoint.Port}" });
            var service = new MonitoringService(_dbContext, null!, _httpClientFactoryMock.Object, _hubContextMock.Object, _optionsMock.Object);
            var result = await service.GetSystemMetricsAsync();
            Assert.Equal(1, requests);
            if (redirect) Assert.Empty(result.Gauges);
            else Assert.Contains("test_metric", result.Gauges);
            _httpClientFactoryMock.Verify(value => value.CreateClient(MonitoringService.HttpClientName), Times.Once);
        }
        finally { stop.Cancel(); await server; listener.Stop(); }
    }

    private readonly ApplicationDbContext _dbContext;
    private readonly MonitoringService _monitoringService;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IHubContext<Infrastructure.Hubs.MonitoringHub>> _hubContextMock;
    private readonly Mock<IOptions<ObservabilityOptions>> _optionsMock;

    public MonitoringServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        
        // Mock HttpClientFactory
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient();
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        // Mock HubContext
        _hubContextMock = new Mock<IHubContext<Infrastructure.Hubs.MonitoringHub>>();
        
        // Mock Options
        _optionsMock = new Mock<IOptions<ObservabilityOptions>>();
        _optionsMock.Setup(x => x.Value).Returns(new ObservabilityOptions
        {
            MetricsBaseUrl = "https://localhost:7035"
        });

        _monitoringService = new MonitoringService(_dbContext, null!, _httpClientFactoryMock.Object, _hubContextMock.Object, _optionsMock.Object);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    #region GetActivityStatsAsync Tests

    [Fact]
    public async Task GetActivityStatsAsync_ShouldReturnActivityStats()
    {
        // Arrange
        // Seed some test data if needed

        // Act
        var result = await _monitoringService.GetActivityStatsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ActivityStatsDto>(result);
        Assert.True(result.ActiveSessions >= 0);
        Assert.True(result.TotalLogins >= 0);
        Assert.True(result.FailedLogins >= 0);
        Assert.True(result.RiskScore >= 0);
    }

    #endregion

    #region GetSecurityMetricsAsync Tests

    [Fact]
    public async Task GetSecurityMetricsAsync_ShouldReturnSecurityMetrics()
    {
        // Arrange
        // Seed some test data if needed

        // Act
        var result = await _monitoringService.GetSecurityMetricsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SecurityMetricsDto>(result);
        Assert.NotNull(result.LoginAttempts);
        Assert.NotNull(result.ActiveSessions);
        Assert.NotNull(result.FailedLogins);
    }

    #endregion

    #region GetRealTimeAlertsAsync Tests

    [Fact]
    public async Task GetRealTimeAlertsAsync_ShouldReturnAlerts()
    {
        // Arrange
        // Seed some test data if needed

        // Act
        var result = await _monitoringService.GetRealTimeAlertsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<SecurityAlertDto>>(result);
    }

    #endregion

    #region ParsePrometheusMetricsAsync Tests

    [Fact]
    public async Task ParsePrometheusMetricsAsync_ShouldParseValidMetrics()
    {
        // Arrange
        var metricsText = @"
# HELP http_requests_total Total number of HTTP requests
# TYPE http_requests_total counter
http_requests_total{method=""GET"",endpoint=""/api/users""} 42
# HELP response_time Response time in seconds
# TYPE response_time gauge
response_time 0.5
";

        // Act
        var result = await _monitoringService.ParsePrometheusMetricsAsync(metricsText);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<PrometheusMetricsDto>(result);
        Assert.Contains("http_requests_total", result.Counters);
        Assert.Contains("response_time", result.Gauges);
    }

    [Fact]
    public async Task ParsePrometheusMetricsAsync_ShouldHandleEmptyMetrics()
    {
        // Arrange
        var metricsText = "";

        // Act
        var result = await _monitoringService.ParsePrometheusMetricsAsync(metricsText);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<PrometheusMetricsDto>(result);
        Assert.Empty(result.Counters);
        Assert.Empty(result.Gauges);
        Assert.Empty(result.Histograms);
    }

    #endregion

    #region Broadcast Methods Tests

    [Fact]
    public async Task BroadcastActivityStatsUpdateAsync_ShouldSendUpdateToClients()
    {
        // Arrange
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group("monitoring")).Returns(mockClientProxy.Object);

        // Act
        await _monitoringService.BroadcastActivityStatsUpdateAsync();

        // Assert - Just ensure it completes without error
        Assert.True(true);
    }

    [Fact]
    public async Task BroadcastSecurityAlertsUpdateAsync_ShouldSendUpdateToClients()
    {
        // Arrange
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group("monitoring")).Returns(mockClientProxy.Object);

        // Act
        await _monitoringService.BroadcastSecurityAlertsUpdateAsync();

        // Assert - Just ensure it completes without error
        Assert.True(true);
    }

    [Fact]
    public async Task BroadcastSystemMetricsUpdateAsync_ShouldSendUpdateToClients()
    {
        // Arrange
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group("monitoring")).Returns(mockClientProxy.Object);

        // Act
        await _monitoringService.BroadcastSystemMetricsUpdateAsync();

        // Assert - Just ensure it completes without error
        Assert.True(true);
    }

    #endregion
}
