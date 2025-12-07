using Core.Application;
using Core.Domain.Constants;
using Infrastructure.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests.BackgroundServices;

public class MonitoringBackgroundServiceTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceScope> _scopeMock;
    private readonly Mock<IMonitoringService> _monitoringServiceMock;
    private readonly Mock<ISettingsService> _settingsServiceMock;
    private readonly Mock<ILogger<MonitoringBackgroundService>> _loggerMock;

    public MonitoringBackgroundServiceTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeMock = new Mock<IServiceScope>();
        _monitoringServiceMock = new Mock<IMonitoringService>();
        _settingsServiceMock = new Mock<ISettingsService>();
        _loggerMock = new Mock<ILogger<MonitoringBackgroundService>>();

        // Setup Scope Factory
        _serviceProviderMock.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_scopeFactoryMock.Object);
        _scopeFactoryMock.Setup(x => x.CreateScope())
            .Returns(_scopeMock.Object);
        _scopeMock.Setup(x => x.ServiceProvider)
            .Returns(_serviceProviderMock.Object);

        // Setup Service Resolution
        _serviceProviderMock.Setup(x => x.GetService(typeof(IMonitoringService)))
            .Returns(_monitoringServiceMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ISettingsService)))
            .Returns(_settingsServiceMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Skip_Broadcasting_When_Monitoring_Disabled()
    {
        // Arrange
        // Simulate "Enabled = false"
        _settingsServiceMock.Setup(x => x.GetValueAsync<bool>(MonitoringSettings.Enabled, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // Ensure "Enabled" setting exists so the check `!= null` passes logic
         _settingsServiceMock.Setup(x => x.GetValueAsync(MonitoringSettings.Enabled, It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500)); // Run briefly

        var service = new MonitoringBackgroundService(_serviceProviderMock.Object, _loggerMock.Object);

        // Act
        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(1000); // Wait for cancellation
        }
        catch (OperationCanceledException) { }

        // Assert
        // Should NOT have called broadcast
        _monitoringServiceMock.Verify(x => x.BroadcastActivityStatsUpdateAsync(), Times.Never);
        _monitoringServiceMock.Verify(x => x.BroadcastSecurityAlertsUpdateAsync(), Times.Never);
        _monitoringServiceMock.Verify(x => x.BroadcastSystemMetricsUpdateAsync(), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Broadcast_When_Monitoring_Enabled()
    {
        // Arrange
        // Simulate "Enabled = true"
        _settingsServiceMock.Setup(x => x.GetValueAsync<bool>(MonitoringSettings.Enabled, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _settingsServiceMock.Setup(x => x.GetValueAsync(MonitoringSettings.Enabled, It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");
        
        // Mock Intervals to be very short for test
        _settingsServiceMock.Setup(x => x.GetValueAsync<int>(MonitoringSettings.ActivityIntervalSeconds, It.IsAny<CancellationToken>())).ReturnsAsync(0); // Uses default 5s if 0? No my code says `> 0 ? a : 5`.
        // Wait, I need them to trigger quickly. I can't set them < 5s easily with current logic `a > 0 ? a : 5`.
        // Actually, if I return 1, it will be 1 second.
        _settingsServiceMock.Setup(x => x.GetValueAsync<int>(MonitoringSettings.ActivityIntervalSeconds, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _settingsServiceMock.Setup(x => x.GetValueAsync<int>(MonitoringSettings.SecurityIntervalSeconds, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _settingsServiceMock.Setup(x => x.GetValueAsync<int>(MonitoringSettings.MetricsIntervalSeconds, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        using var cts = new CancellationTokenSource();
        // Cancellation needs to be longer than the interval (1s) + initial delay (2s)
        // Code has `await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);` at start.
        // So I need to wait at least 3 seconds.
        cts.CancelAfter(TimeSpan.FromSeconds(4)); 

        var service = new MonitoringBackgroundService(_serviceProviderMock.Object, _loggerMock.Object);

        // Act
        try
        {
             await service.StartAsync(cts.Token);
             // Verify loop keeps running for a bit
             // The StartAsync returns a Task that completes when the service stops. 
             // But BackgroundService.StartAsync returns when *execute* starts (synchronously mostly) if it awaits? 
             // No, StartAsync returns immediately. ExecuteAsync runs in background.
             // I should call `service.ExecuteAsync` directly via reflection or wrapper? 
             // BackgroundService exposes `ExecuteTask` property? 
             
             // Actually, `StartAsync` starts the task. I can await `service.ExecuteTask` if I can access it.
             // Or just wait.
             await Task.Delay(4500); 
        }
        catch (OperationCanceledException) { }
        finally 
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Assert
        // Should have called broadcast at least once
        _monitoringServiceMock.Verify(x => x.BroadcastActivityStatsUpdateAsync(), Times.AtLeastOnce, "Should broadcast activity stats");
    }
}
