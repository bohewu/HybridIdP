using Core.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundServices;

/// <summary>
/// Background service that periodically broadcasts monitoring data to connected SignalR clients.
/// Provides real-time updates for activity stats, security alerts, and system metrics.
/// </summary>
public class MonitoringBackgroundService : BackgroundService
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MonitoringBackgroundService> logger;
    
    // Update intervals for different data types
    private readonly TimeSpan activityStatsInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan securityAlertsInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan systemMetricsInterval = TimeSpan.FromSeconds(15);
    
    // Timers for tracking last update times
    private DateTime lastActivityStatsUpdate = DateTime.MinValue;
    private DateTime lastSecurityAlertsUpdate = DateTime.MinValue;
    private DateTime lastSystemMetricsUpdate = DateTime.MinValue;

    public MonitoringBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<MonitoringBackgroundService> logger)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Monitoring Background Service started");

        // Wait a bit before starting to allow the application to fully initialize
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Create a scope for dependency injection
                using var scope = serviceProvider.CreateScope();
                var monitoringService = scope.ServiceProvider.GetRequiredService<IMonitoringService>();

                // Update Activity Stats (every 5 seconds)
                if (now - lastActivityStatsUpdate >= activityStatsInterval)
                {
                    await BroadcastActivityStatsAsync(monitoringService);
                    lastActivityStatsUpdate = now;
                }

                // Update Security Alerts (every 10 seconds)
                if (now - lastSecurityAlertsUpdate >= securityAlertsInterval)
                {
                    await BroadcastSecurityAlertsAsync(monitoringService);
                    lastSecurityAlertsUpdate = now;
                }

                // Update System Metrics (every 15 seconds)
                if (now - lastSystemMetricsUpdate >= systemMetricsInterval)
                {
                    await BroadcastSystemMetricsAsync(monitoringService);
                    lastSystemMetricsUpdate = now;
                }

                // Sleep for 1 second to prevent tight loop
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, don't log as error
                logger.LogInformation("Monitoring Background Service is stopping");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred in Monitoring Background Service");
                // Continue running even if an error occurs
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("Monitoring Background Service stopped");
    }

    /// <summary>
    /// Broadcasts activity statistics to all connected clients.
    /// </summary>
    private async Task BroadcastActivityStatsAsync(IMonitoringService monitoringService)
    {
        try
        {
            logger.LogDebug("Broadcasting activity stats update");
            await monitoringService.BroadcastActivityStatsUpdateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast activity stats");
        }
    }

    /// <summary>
    /// Broadcasts security alerts to all connected clients.
    /// </summary>
    private async Task BroadcastSecurityAlertsAsync(IMonitoringService monitoringService)
    {
        try
        {
            logger.LogDebug("Broadcasting security alerts update");
            await monitoringService.BroadcastSecurityAlertsUpdateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast security alerts");
        }
    }

    /// <summary>
    /// Broadcasts system metrics to all connected clients.
    /// </summary>
    private async Task BroadcastSystemMetricsAsync(IMonitoringService monitoringService)
    {
        try
        {
            logger.LogDebug("Broadcasting system metrics update");
            await monitoringService.BroadcastSystemMetricsUpdateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast system metrics");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Monitoring Background Service is stopping gracefully");
        await base.StopAsync(cancellationToken);
    }
}

