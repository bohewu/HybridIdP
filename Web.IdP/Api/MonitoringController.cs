using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Api;

/// <summary>
/// Monitoring endpoints for real-time security dashboard.
/// </summary>
[ApiController]
[Route("api/admin/monitoring")]
[Authorize]
public class MonitoringController : ControllerBase
{
    private readonly IMonitoringService _monitoringService;

    public MonitoringController(IMonitoringService monitoringService)
    {
        _monitoringService = monitoringService;
    }

    /// <summary>
    /// Get real-time activity statistics.
    /// </summary>
    [HttpGet("stats")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<ActivityStatsDto>> GetActivityStats()
    {
        var stats = await _monitoringService.GetActivityStatsAsync();
        
        // Broadcast update to all connected clients
        await _monitoringService.BroadcastActivityStatsUpdateAsync();
        
        return Ok(stats);
    }

    /// <summary>
    /// Get security metrics for monitoring dashboard.
    /// </summary>
    [HttpGet("metrics")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<SecurityMetricsDto>> GetSecurityMetrics()
    {
        var metrics = await _monitoringService.GetSecurityMetricsAsync();
        return Ok(metrics);
    }

    /// <summary>
    /// Get real-time security alerts.
    /// </summary>
    [HttpGet("alerts")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<IEnumerable<SecurityAlertDto>>> GetRealTimeAlerts()
    {
        var alerts = await _monitoringService.GetRealTimeAlertsAsync();
        
        // Broadcast update to all connected clients
        await _monitoringService.BroadcastSecurityAlertsUpdateAsync();
        
        return Ok(alerts);
    }

    /// <summary>
    /// Parse Prometheus metrics from /metrics endpoint.
    /// </summary>
    [HttpPost("metrics/prometheus")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<PrometheusMetricsDto>> ParsePrometheusMetrics([FromBody] PrometheusMetricsRequest request)
    {
        var parsed = await _monitoringService.ParsePrometheusMetricsAsync(request.MetricsText);
        return Ok(parsed);
    }

    /// <summary>
    /// Get system metrics from Prometheus /metrics endpoint.
    /// </summary>
    [HttpGet("system-metrics")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<PrometheusMetricsDto>> GetSystemMetrics()
    {
        var metrics = await _monitoringService.GetSystemMetricsAsync();
        
        // Broadcast update to all connected clients
        await _monitoringService.BroadcastSystemMetricsUpdateAsync();
        
        return Ok(metrics);
    }
}

/// <summary>
/// Request DTO for Prometheus metrics parsing.
/// </summary>
public class PrometheusMetricsRequest
{
    public string MetricsText { get; set; } = string.Empty;
}