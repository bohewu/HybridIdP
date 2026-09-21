namespace Infrastructure.Directory;

/// <summary>
/// Deployment-only directory connection inputs. Secrets and distinguished names are supplied
/// outside committed configuration and are never included in audit records.
/// </summary>
public sealed class DirectoryCredentialTransportOptions
{
    public const string Section = "DirectoryCredentialTransport";

    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? BaseDistinguishedName { get; set; }
    public string? ManagedSearchFilter { get; set; }
    public string? WindowsDomain { get; set; }
    public string? ServiceAccountUserName { get; set; }
    public string? ServiceAccountDistinguishedName { get; set; }
    public string? ServiceAccountSecret { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    internal bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(BaseDistinguishedName) &&
        !string.IsNullOrWhiteSpace(ManagedSearchFilter) &&
        Timeout > TimeSpan.Zero;
}
