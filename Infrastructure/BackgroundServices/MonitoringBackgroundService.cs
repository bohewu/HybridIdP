using Core.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundServices;

/// <summary>
/// Background service that periodically broadcasts monitoring data to connected SignalR clients.
/// Provides real-time updates for activity stats, security alerts, and system metrics.
/// </summary>
public partial class MonitoringBackgroundService : BackgroundService
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
        LogServiceStarted(logger);

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
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                // Check if monitoring is enabled (default to true)
                var isEnabled = await settingsService.GetValueAsync<bool>(Core.Domain.Constants.MonitoringSettings.Enabled);
                // If setting doesn't exist (null), treat as true (default)
                if (await settingsService.GetValueAsync(Core.Domain.Constants.MonitoringSettings.Enabled) != null && !isEnabled) 
                {
                    // Monitoring disabled, sleep and continue
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                // Get dynamic intervals (with defaults)
                var activityInterval = TimeSpan.FromSeconds(await settingsService.GetValueAsync<int>(Core.Domain.Constants.MonitoringSettings.ActivityIntervalSeconds) is int a && a > 0 ? a : 5);
                var securityInterval = TimeSpan.FromSeconds(await settingsService.GetValueAsync<int>(Core.Domain.Constants.MonitoringSettings.SecurityIntervalSeconds) is int s && s > 0 ? s : 10);
                var metricsInterval = TimeSpan.FromSeconds(await settingsService.GetValueAsync<int>(Core.Domain.Constants.MonitoringSettings.MetricsIntervalSeconds) is int m && m > 0 ? m : 15);

                // Update Activity Stats
                if (now - lastActivityStatsUpdate >= activityInterval)
                {
                    await BroadcastActivityStatsAsync(monitoringService);
                    lastActivityStatsUpdate = now;
                }

                // Update Security Alerts
                if (now - lastSecurityAlertsUpdate >= securityInterval)
                {
                    await BroadcastSecurityAlertsAsync(monitoringService);
                    lastSecurityAlertsUpdate = now;
                }

                // Update System Metrics
                if (now - lastSystemMetricsUpdate >= metricsInterval)
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
                LogServiceStopping(logger);
                break;
            }
            catch (Exception ex)
            {
                LogServiceError(logger, ex);
                // Continue running even if an error occurs
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        LogServiceStopped(logger);
    }

    /// <summary>
    /// Broadcasts activity statistics to all connected clients.
    /// </summary>
    private async Task BroadcastActivityStatsAsync(IMonitoringService monitoringService)
    {
        try
        {
            LogBroadcastingActivity(logger);
            await monitoringService.BroadcastActivityStatsUpdateAsync();
        }
        catch (Exception ex)
        {
            LogActivityBroadcastFailed(logger, ex);
        }
    }

    /// <summary>
    /// Broadcasts security alerts to all connected clients.
    /// </summary>
    private async Task BroadcastSecurityAlertsAsync(IMonitoringService monitoringService)
    {
        try
        {
            LogBroadcastingSecurity(logger);
            await monitoringService.BroadcastSecurityAlertsUpdateAsync();
        }
        catch (Exception ex)
        {
            LogSecurityBroadcastFailed(logger, ex);
        }
    }

    /// <summary>
    /// Broadcasts system metrics to all connected clients.
    /// </summary>
    private async Task BroadcastSystemMetricsAsync(IMonitoringService monitoringService)
    {
        try
        {
            LogBroadcastingSystem(logger);
            await monitoringService.BroadcastSystemMetricsUpdateAsync();
        }
        catch (Exception ex)
        {
            LogSystemBroadcastFailed(logger, ex);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogServiceStoppingGracefully(logger);
        await base.StopAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitoring Background Service started")]
    static partial void LogServiceStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitoring Background Service is stopping")]
    static partial void LogServiceStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error occurred in Monitoring Background Service")]
    static partial void LogServiceError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitoring Background Service stopped")]
    static partial void LogServiceStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcasting activity stats update")]
    static partial void LogBroadcastingActivity(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to broadcast activity stats")]
    static partial void LogActivityBroadcastFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcasting security alerts update")]
    static partial void LogBroadcastingSecurity(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to broadcast security alerts")]
    static partial void LogSecurityBroadcastFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcasting system metrics update")]
    static partial void LogBroadcastingSystem(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to broadcast system metrics")]
    static partial void LogSystemBroadcastFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitoring Background Service is stopping gracefully")]
    static partial void LogServiceStoppingGracefully(ILogger logger);
}

