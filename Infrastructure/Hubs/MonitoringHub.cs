using Microsoft.AspNetCore.SignalR;

namespace Infrastructure.Hubs;

/// <summary>
/// SignalR hub for real-time monitoring updates.
/// Clients connect to this hub to receive periodic updates from MonitoringBackgroundService.
/// </summary>
public class MonitoringHub : Hub
{
    // No dependencies needed - this hub only manages connections and groups.
    // The MonitoringBackgroundService broadcasts updates via IHubContext.
    public MonitoringHub()
    {
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
}
