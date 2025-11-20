using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Application.DTOs;

namespace Core.Application;

/// <summary>
/// Service interface for dashboard operations.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets basic dashboard statistics.
    /// </summary>
    Task<DashboardStatsDto> GetDashboardStatsAsync();

    /// <summary>
    /// Gets activity statistics for monitoring.
    /// </summary>
    Task<ActivityStatsDto> GetActivityStatsAsync();

    /// <summary>
    /// Gets security metrics for charts.
    /// </summary>
    Task<SecurityMetricsDto> GetSecurityMetricsAsync();

    /// <summary>
    /// Gets list of active sessions.
    /// </summary>
    Task<IEnumerable<SessionDto>> GetActiveSessionsAsync();

    /// <summary>
    /// Gets recent failed login attempts.
    /// </summary>
    Task<IEnumerable<FailedLoginDto>> GetFailedLoginAttemptsAsync(int limit = 50);

    /// <summary>
    /// Terminates a user session.
    /// </summary>
    Task TerminateSessionAsync(string sessionId);
}