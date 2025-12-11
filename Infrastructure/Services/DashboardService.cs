using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Services;

/// <summary>
/// Service for dashboard operations.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;

    public DashboardService(
        ApplicationDbContext dbContext,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        IOpenIddictAuthorizationManager authorizationManager)
    {
        _dbContext = dbContext;
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _authorizationManager = authorizationManager;
    }

    /// <inheritdoc />
    public async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        var totalClients = 0;
        await foreach (var _ in _applicationManager.ListAsync())
        {
            totalClients++;
        }

        var totalScopes = 0;
        await foreach (var _ in _scopeManager.ListAsync())
        {
            totalScopes++;
        }

        var totalUsers = await _dbContext.Users.CountAsync();

        return new DashboardStatsDto
        {
            TotalClients = totalClients,
            TotalScopes = totalScopes,
            TotalUsers = totalUsers
        };
    }

    /// <inheritdoc />
    public async Task<ActivityStatsDto> GetActivityStatsAsync()
    {
        var now = DateTime.UtcNow;
        var last24Hours = now.AddHours(-24);

        // Active sessions - count of successful logins in last 24 hours (approximation)
        var activeSessions = await _dbContext.LoginHistories
            .Where(l => l.IsSuccessful && l.LoginTime >= last24Hours)
            .CountAsync();

        // Total logins in last 24 hours
        var totalLogins = await _dbContext.LoginHistories
            .Where(l => l.IsSuccessful && l.LoginTime >= last24Hours)
            .CountAsync();

        // Failed logins in last 24 hours
        var failedLogins = await _dbContext.LoginHistories
            .Where(l => !l.IsSuccessful && l.LoginTime >= last24Hours)
            .CountAsync();

        var riskScore = await CalculateRiskScoreAsync();

        return new ActivityStatsDto
        {
            ActiveSessions = activeSessions,
            TotalLogins = totalLogins,
            FailedLogins = failedLogins,
            RiskScore = riskScore
        };
    }

    /// <inheritdoc />
    public async Task<SecurityMetricsDto> GetSecurityMetricsAsync()
    {
        // Get last 7 days data
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-7);

        var loginAttempts = new List<int>();
        var failedLogins = new List<int>();
        var activeSessions = new List<int>();

        for (int i = 6; i >= 0; i--)
        {
            var dayStart = startDate.AddDays(i);
            var dayEnd = dayStart.AddDays(1);

            var attempts = await _dbContext.LoginHistories.CountAsync(lh => lh.LoginTime >= dayStart && lh.LoginTime < dayEnd);
            var failed = await _dbContext.LoginHistories.CountAsync(lh => lh.LoginTime >= dayStart && lh.LoginTime < dayEnd && !lh.IsSuccessful);
            var sessions = await _dbContext.LoginHistories.CountAsync(lh => lh.LoginTime >= dayStart && lh.LoginTime < dayEnd && lh.IsSuccessful); // Approximation

            loginAttempts.Add(attempts);
            failedLogins.Add(failed);
            activeSessions.Add(sessions);
        }

        return new SecurityMetricsDto
        {
            LoginAttempts = loginAttempts,
            FailedLogins = failedLogins,
            ActiveSessions = activeSessions
        };
    }

    /// <inheritdoc />
    public Task<IEnumerable<SessionDto>> GetActiveSessionsAsync()
    {
        // For now, return empty. Can be implemented later with OpenIddict authorizations
        return Task.FromResult<IEnumerable<SessionDto>>([]);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<FailedLoginDto>> GetFailedLoginAttemptsAsync(int limit = 50)
    {
        var failedLogins = await _dbContext.LoginHistories
            .Where(lh => !lh.IsSuccessful)
            .OrderByDescending(lh => lh.LoginTime)
            .Take(limit)
            .Select(lh => new FailedLoginDto
            {
                Id = lh.Id,
                UserId = lh.UserId,
                UserEmail = lh.User!.Email,
                LoginTime = lh.LoginTime,
                IpAddress = lh.IpAddress,
                UserAgent = lh.UserAgent,
                RiskScore = lh.RiskScore
            })
            .ToListAsync();

        return failedLogins;
    }

    /// <inheritdoc />
    public Task TerminateSessionAsync(string sessionId)
    {
        // TODO: Implement session termination using OpenIddict
        throw new NotImplementedException("Session termination not yet implemented");
    }

    private async Task<decimal> CalculateRiskScoreAsync()
    {
        var recentFailedLogins = await _dbContext.LoginHistories
            .Where(lh => !lh.IsSuccessful && lh.LoginTime > DateTime.UtcNow.AddHours(-1))
            .CountAsync();

        // Simple risk score based on recent failures
        return Math.Min(recentFailedLogins * 10, 100);
    }
}