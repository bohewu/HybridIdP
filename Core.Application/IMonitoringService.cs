using Core.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Application
{
    public interface IMonitoringService
    {
        /// <summary>
        /// Gets real-time activity statistics.
        /// </summary>
        /// <returns>Activity statistics</returns>
        Task<ActivityStatsDto> GetActivityStatsAsync();

        /// <summary>
        /// Gets security metrics for monitoring.
        /// </summary>
        /// <returns>Security metrics</returns>
        Task<SecurityMetricsDto> GetSecurityMetricsAsync();

        /// <summary>
        /// Gets real-time alerts.
        /// </summary>
        /// <returns>List of active alerts</returns>
        Task<IEnumerable<SecurityAlertDto>> GetRealTimeAlertsAsync();

        /// <summary>
        /// Parses Prometheus metrics from /metrics endpoint.
        /// </summary>
        /// <param name="metricsText">Raw Prometheus metrics text</param>
        /// <returns>Parsed metrics</returns>
        Task<PrometheusMetricsDto> ParsePrometheusMetricsAsync(string metricsText);

        /// <summary>
        /// Gets system metrics from Prometheus /metrics endpoint.
        /// </summary>
        /// <returns>Parsed system metrics</returns>
        Task<PrometheusMetricsDto> GetSystemMetricsAsync();

        /// <summary>
        /// Broadcasts activity statistics update to connected clients.
        /// </summary>
        /// <returns>Task</returns>
        Task BroadcastActivityStatsUpdateAsync();

        /// <summary>
        /// Broadcasts security alerts update to connected clients.
        /// </summary>
        /// <returns>Task</returns>
        Task BroadcastSecurityAlertsUpdateAsync();

        /// <summary>
        /// Broadcasts system metrics update to connected clients.
        /// </summary>
        /// <returns>Task</returns>
        Task BroadcastSystemMetricsUpdateAsync();
    }
}