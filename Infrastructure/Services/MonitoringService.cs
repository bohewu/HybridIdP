using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Core.Domain.Events;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Services;

public class MonitoringService : IMonitoringService
{
    private readonly IApplicationDbContext _db;
    private readonly IDomainEventPublisher _eventPublisher;

    public MonitoringService(IApplicationDbContext db, IDomainEventPublisher eventPublisher)
    {
        _db = db;
        _eventPublisher = eventPublisher;
    }

    public async Task<ActivityStatsDto> GetActivityStatsAsync()
    {
        // TODO: Implement real logic to calculate activity stats
        // For now, return mock data
        return new ActivityStatsDto
        {
            ActiveSessions = 42,
            TotalLogins = 1250,
            FailedLogins = 15,
            RiskScore = 2.3m
        };
    }

    public async Task<SecurityMetricsDto> GetSecurityMetricsAsync()
    {
        // TODO: Implement real logic to gather security metrics
        // For now, return mock data
        return new SecurityMetricsDto
        {
            LoginAttempts = new List<int> { 120, 135, 142, 158, 145, 162, 178 },
            ActiveSessions = new List<int> { 25, 32, 28, 45, 38, 42, 50 },
            FailedLogins = new List<int> { 2, 3, 1, 5, 2, 4, 3 }
        };
    }

    public async Task<IEnumerable<SecurityAlertDto>> GetRealTimeAlertsAsync()
    {
        // TODO: Implement real logic to fetch real-time alerts
        // For now, return mock alerts
        return new List<SecurityAlertDto>
        {
            new SecurityAlertDto
            {
                Id = 1,
                Type = "warning",
                Message = "Multiple failed login attempts from IP 192.168.1.100",
                Timestamp = DateTime.UtcNow,
                Severity = "medium"
            },
            new SecurityAlertDto
            {
                Id = 2,
                Type = "danger",
                Message = "Suspicious activity detected for user admin@hybridauth.local",
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                Severity = "high"
            }
        };
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
}