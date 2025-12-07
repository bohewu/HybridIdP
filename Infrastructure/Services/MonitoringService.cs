using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Core.Domain.Events;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options; // Added
using Core.Application.Options; // Added

namespace Infrastructure.Services;

public class MonitoringService : IMonitoringService
{
    private readonly IApplicationDbContext _db;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<Infrastructure.Hubs.MonitoringHub> _hubContext;
    private readonly ObservabilityOptions _options; // Changed

    public MonitoringService(
        IApplicationDbContext db, 
        IDomainEventPublisher eventPublisher, 
        IHttpClientFactory httpClientFactory, 
        IHubContext<Infrastructure.Hubs.MonitoringHub> hubContext, 
        IOptions<ObservabilityOptions> options) // Changed
    {
        _db = db;
        _eventPublisher = eventPublisher;
        _httpClientFactory = httpClientFactory;
        _hubContext = hubContext;
        _options = options.Value; // Changed
    }

    public async Task<ActivityStatsDto> GetActivityStatsAsync()
    {
        // Get real data from database
        var now = DateTime.UtcNow;
        var last24Hours = now.AddHours(-24);

        // Active sessions - count of successful logins in last 24 hours
        var activeSessions = await _db.LoginHistories
            .Where(l => l.IsSuccessful && l.LoginTime >= last24Hours)
            .CountAsync();

        // Total logins in last 24 hours
        var totalLogins = await _db.LoginHistories
            .Where(l => l.IsSuccessful && l.LoginTime >= last24Hours)
            .CountAsync();

        // Failed logins in last 24 hours
        var failedLogins = await _db.LoginHistories
            .Where(l => !l.IsSuccessful && l.LoginTime >= last24Hours)
            .CountAsync();

        // Average risk score from recent logins
        var riskScores = await _db.LoginHistories
            .Where(l => l.LoginTime >= last24Hours)
            .Select(l => (decimal)l.RiskScore)
            .ToListAsync();
        var avgRiskScore = riskScores.Any() ? riskScores.Average() : 0;

        return new ActivityStatsDto
        {
            ActiveSessions = activeSessions,
            TotalLogins = totalLogins,
            FailedLogins = failedLogins,
            RiskScore = Math.Round(avgRiskScore, 2)
        };
    }

    public async Task<SecurityMetricsDto> GetSecurityMetricsAsync()
    {
        var now = DateTime.UtcNow;
        var metrics = new SecurityMetricsDto();

        // Get data for last 7 days
        for (int i = 6; i >= 0; i--)
        {
            var date = now.AddDays(-i).Date;
            var nextDate = date.AddDays(1);

            // Login attempts (all login events)
            var loginAttempts = await _db.LoginHistories
                .Where(l => l.LoginTime >= date && l.LoginTime < nextDate)
                .CountAsync();

            // Active sessions (successful logins)
            var activeSessions = await _db.LoginHistories
                .Where(l => l.IsSuccessful && l.LoginTime >= date && l.LoginTime < nextDate)
                .CountAsync();

            // Failed logins
            var failedLogins = await _db.LoginHistories
                .Where(l => !l.IsSuccessful && l.LoginTime >= date && l.LoginTime < nextDate)
                .CountAsync();

            metrics.LoginAttempts.Add(loginAttempts);
            metrics.ActiveSessions.Add(activeSessions);
            metrics.FailedLogins.Add(failedLogins);
        }

        return metrics;
    }

    public async Task<IEnumerable<SecurityAlertDto>> GetRealTimeAlertsAsync()
    {
        var alerts = new List<SecurityAlertDto>();
        var now = DateTime.UtcNow;
        var lastHour = now.AddHours(-1);

        // Get recent abnormal logins
        var abnormalLogins = await _db.LoginHistories
            .Where(l => l.IsFlaggedAbnormal && l.LoginTime >= lastHour)
            .OrderByDescending(l => l.LoginTime)
            .Take(5)
            .ToListAsync();

        foreach (var login in abnormalLogins)
        {
            alerts.Add(new SecurityAlertDto
            {
                Id = login.Id,
                Type = "danger",
                Message = $"Suspicious login activity detected for user {login.UserId} from IP {login.IpAddress}",
                Timestamp = login.LoginTime,
                Severity = login.RiskScore > 70 ? "high" : "medium"
            });
        }

        // Get recent failed login attempts from same IP (potential brute force)
        var failedLoginGroups = await _db.LoginHistories
            .Where(l => !l.IsSuccessful && l.LoginTime >= lastHour && l.IpAddress != null)
            .GroupBy(l => l.IpAddress)
            .Where(g => g.Count() >= 3)
            .Select(g => new { IpAddress = g.Key, Count = g.Count(), LatestTime = g.Max(l => l.LoginTime) })
            .ToListAsync();

        foreach (var group in failedLoginGroups)
        {
            alerts.Add(new SecurityAlertDto
            {
                Id = Math.Abs(group.IpAddress!.GetHashCode()), // Simple ID generation
                Type = "warning",
                Message = $"Multiple failed login attempts from IP {group.IpAddress} ({group.Count} attempts)",
                Timestamp = group.LatestTime,
                Severity = group.Count >= 5 ? "high" : "medium"
            });
        }

        // Get recent security-related audit events
        var securityEvents = await _db.AuditEvents
            .Where(e => e.Timestamp >= lastHour && 
                       (e.EventType.Contains("Security") || e.EventType.Contains("Policy") || e.EventType.Contains("Failed")))
            .OrderByDescending(e => e.Timestamp)
            .Take(3)
            .ToListAsync();

        foreach (var auditEvent in securityEvents)
        {
            alerts.Add(new SecurityAlertDto
            {
                Id = auditEvent.Id,
                Type = "info",
                Message = $"{auditEvent.EventType}: {auditEvent.Details}",
                Timestamp = auditEvent.Timestamp,
                Severity = "low"
            });
        }

        return alerts.OrderByDescending(a => a.Timestamp).Take(10);
    }

    public async Task<PrometheusMetricsDto> ParsePrometheusMetricsAsync(string metricsText)
    {
        var result = new PrometheusMetricsDto();
        var metricTypes = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(metricsText))
        {
            return result;
        }

        var lines = metricsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            if (trimmedLine.StartsWith("# TYPE "))
            {
                // Parse TYPE line: # TYPE metric_name type
                var parts = trimmedLine.Split(' ', 4);
                if (parts.Length >= 4)
                {
                    var metricName = parts[2];
                    var type = parts[3];
                    metricTypes[metricName] = type;
                }
            }
            else if (!trimmedLine.StartsWith("#"))
            {
                // Parse metric line: metric_name{label="value"} value
                var parts = trimmedLine.Split(' ', 2);
                if (parts.Length == 2)
                {
                    var metricPart = parts[0];
                    var valuePart = parts[1];

                    // Extract metric name (before any labels)
                    var metricName = metricPart.Contains('{') 
                        ? metricPart.Substring(0, metricPart.IndexOf('{'))
                        : metricPart;

                    var metricType = metricTypes.GetValueOrDefault(metricName, "gauge");

                    if (metricType == "counter" && long.TryParse(valuePart, out var longValue))
                    {
                        result.Counters[metricName] = longValue;
                    }
                    else if (double.TryParse(valuePart, out var value))
                    {
                        result.Gauges[metricName] = value;
                    }
                }
            }
        }

        return result;
    }

    public async Task<PrometheusMetricsDto> GetSystemMetricsAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = _options.MetricsBaseUrl; // Changed
            var response = await client.GetAsync($"{baseUrl.TrimEnd('/')}/metrics");
            response.EnsureSuccessStatusCode();
            
            var metricsText = await response.Content.ReadAsStringAsync();
            return await ParsePrometheusMetricsAsync(metricsText);
        }
        catch (Exception)
        {
            // If metrics endpoint is not available, return empty metrics
            return new PrometheusMetricsDto();
        }
    }

    public async Task BroadcastActivityStatsUpdateAsync()
    {
        var stats = await GetActivityStatsAsync();
        await _hubContext.Clients.Group("monitoring").SendAsync("ActivityStatsUpdated", stats);
    }

    public async Task BroadcastSecurityAlertsUpdateAsync()
    {
        var alerts = await GetRealTimeAlertsAsync();
        await _hubContext.Clients.Group("monitoring").SendAsync("SecurityAlertsUpdated", alerts);
    }

    public async Task BroadcastSystemMetricsUpdateAsync()
    {
        var metrics = await GetSystemMetricsAsync();
        await _hubContext.Clients.Group("monitoring").SendAsync("SystemMetricsUpdated", metrics);
    }
}