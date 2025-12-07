namespace Core.Application.Options;

public class ObservabilityOptions
{
    public const string MonitoringSection = "Monitoring";
    public const string ObservabilitySection = "Observability";

    public string MetricsBaseUrl { get; set; } = "https://localhost:7035";
    public string[] AllowedIPs { get; set; } = Array.Empty<string>();
    public bool PrometheusEnabled { get; set; } // Added
}
