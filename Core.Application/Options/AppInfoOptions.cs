namespace Core.Application.Options;

public class AppInfoOptions
{
    public const string Section = "AppInfo";
    public string ServiceName { get; set; } = "HybridAuthIdP";
    public string ServiceVersion { get; set; } = "1.0.0";
}
