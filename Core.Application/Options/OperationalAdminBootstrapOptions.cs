namespace Core.Application.Options;

/// <summary>
/// Configuration for the one-time operational administrator bootstrap.
/// </summary>
public sealed class OperationalAdminBootstrapOptions
{
    public const string Section = "OperationalAdminBootstrap";

    public bool Enabled { get; set; }

    /// <summary>
    /// Hex-encoded SHA-256 digest of the operator-provided bootstrap token.
    /// </summary>
    public string? TokenSha256Digest { get; set; }

    /// <summary>
    /// Absolute UTC expiry of the bootstrap capability.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
