using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Monitoring endpoints for real-time security dashboard.
/// </summary>
[ApiController]
[Route("api/admin/monitoring")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public class MonitoringController : ControllerBase
{
    private readonly IMonitoringService _monitoringService;
    private readonly IDashboardService _dashboardService;

    public MonitoringController(IMonitoringService monitoringService, IDashboardService dashboardService)
    {
        _monitoringService = monitoringService;
        _dashboardService = dashboardService;
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

    /// <summary>
    /// Get dashboard activity stats.
    /// </summary>
    [HttpGet("dashboard/activity-stats")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<ActivityStatsDto>> GetDashboardActivityStats()
    {
        var stats = await _dashboardService.GetActivityStatsAsync();
        return Ok(stats);
    }

    /// <summary>
    /// Get dashboard security metrics.
    /// </summary>
    [HttpGet("dashboard/security-metrics")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<SecurityMetricsDto>> GetDashboardSecurityMetrics()
    {
        var metrics = await _dashboardService.GetSecurityMetricsAsync();
        return Ok(metrics);
    }

    /// <summary>
    /// Get active sessions for dashboard.
    /// </summary>
    [HttpGet("dashboard/active-sessions")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<IEnumerable<SessionDto>>> GetActiveSessions()
    {
        var sessions = await _dashboardService.GetActiveSessionsAsync();
        return Ok(sessions);
    }

    /// <summary>
    /// Get failed login attempts for dashboard.
    /// </summary>
    [HttpGet("dashboard/failed-logins")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<ActionResult<IEnumerable<FailedLoginDto>>> GetFailedLoginAttempts([FromQuery] int limit = 50)
    {
        var failedLogins = await _dashboardService.GetFailedLoginAttemptsAsync(limit);
        return Ok(failedLogins);
    }

    /// <summary>
    /// Terminate a user session.
    /// </summary>
    [HttpPost("dashboard/terminate-session/{sessionId}")]
    [HasPermission(Permissions.Monitoring.Read)]
    public async Task<IActionResult> TerminateSession(string sessionId)
    {
        await _dashboardService.TerminateSessionAsync(sessionId);
        return Ok();
    }
}

/// <summary>
/// Request DTO for Prometheus metrics parsing.
/// </summary>
public class PrometheusMetricsRequest
{
    public string MetricsText { get; set; } = string.Empty;
}
