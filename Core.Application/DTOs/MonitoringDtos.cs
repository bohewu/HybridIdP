namespace Core.Application.DTOs;

/// <summary>
/// DTO for activity statistics.
/// </summary>
public sealed class ActivityStatsDto
{
    public int ActiveSessions { get; set; }
    public int TotalLogins { get; set; }
    public int FailedLogins { get; set; }
    public decimal RiskScore { get; set; }
}

/// <summary>
/// DTO for security metrics.
/// </summary>
public sealed class SecurityMetricsDto
{
    public List<int> LoginAttempts { get; set; } = new();
    public List<int> ActiveSessions { get; set; } = new();
    public List<int> FailedLogins { get; set; } = new();
}

/// <summary>
/// DTO for security alerts.
/// </summary>
public sealed class SecurityAlertDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Severity { get; set; } = string.Empty;
}

/// <summary>
/// DTO for parsed Prometheus metrics.
/// </summary>
public sealed class PrometheusMetricsDto
{
    public Dictionary<string, double> Gauges { get; set; } = new();
    public Dictionary<string, long> Counters { get; set; } = new();
    public Dictionary<string, List<double>> Histograms { get; set; } = new();
}