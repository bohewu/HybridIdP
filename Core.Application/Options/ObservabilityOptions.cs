namespace Core.Application.Options;

public class ObservabilityOptions
{
    public const string MonitoringSection = "Monitoring";
    public const string ObservabilitySection = "Observability";

    public string MetricsBaseUrl { get; set; } = "https://localhost:7035";
    public List<string> AllowedIPs { get; set; } = new();
    public bool PrometheusEnabled { get; set; } // Added
}
