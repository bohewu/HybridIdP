namespace Infrastructure.Options;

/// <summary>Deployment-only approval for manual profile reads, independent of directory authentication.</summary>
public sealed class DirectoryNativeProfileOptions
{
    public const string Section = "DirectoryNativeProfile";
    public bool Enabled { get; set; }
    public bool Trusted { get; set; }
    public bool Paused { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string ProviderNamespace { get; set; } = string.Empty;
    public string PolicyRevision { get; set; } = string.Empty;
    // Explicitly declares which approved query attribute means exact sAMAccountName in this directory.
    // Supported: accountName or employeeId. No inference, rewriting, or alternate-attribute lookup.
    public string SamAccountNameQueryAttribute { get; set; } = string.Empty;
    public string[] AllowedValueSources { get; set; } = [];
}
