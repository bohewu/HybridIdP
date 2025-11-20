using Microsoft.AspNetCore.SignalR;
using Core.Application;
using Core.Application.DTOs;
using System.Threading.Tasks;

namespace Infrastructure.Hubs;

/// <summary>
/// SignalR hub for real-time monitoring updates.
/// </summary>
public class MonitoringHub : Hub
{
    private readonly IMonitoringService _monitoringService;

    public MonitoringHub(IMonitoringService monitoringService)
    {
        _monitoringService = monitoringService;
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // Add client to monitoring group
        await Groups.AddToGroupAsync(Context.ConnectionId, "monitoring");
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Remove client from monitoring group
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "monitoring");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Broadcast activity statistics update to all connected clients.
    /// </summary>
    public async Task BroadcastActivityStatsUpdate(ActivityStatsDto stats)
    {
        await Clients.Group("monitoring").SendAsync("ActivityStatsUpdated", stats);
    }

    /// <summary>
    /// Broadcast security alerts update to all connected clients.
    /// </summary>
    public async Task BroadcastSecurityAlertsUpdate(IEnumerable<SecurityAlertDto> alerts)
    {
        await Clients.Group("monitoring").SendAsync("SecurityAlertsUpdated", alerts);
    }

    /// <summary>
    /// Broadcast system metrics update to all connected clients.
    /// </summary>
    public async Task BroadcastSystemMetricsUpdate(PrometheusMetricsDto metrics)
    {
        await Clients.Group("monitoring").SendAsync("SystemMetricsUpdated", metrics);
    }
}