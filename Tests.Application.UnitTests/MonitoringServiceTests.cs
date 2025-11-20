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

namespace Tests.Application.UnitTests;

public class MonitoringServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MonitoringService _monitoringService;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

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
        
        _monitoringService = new MonitoringService(_dbContext, null!, _httpClientFactoryMock.Object);
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
}