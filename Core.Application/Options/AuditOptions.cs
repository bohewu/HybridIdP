namespace Core.Application.Options;

/// <summary>
/// Audit logging configuration options.
/// </summary>
public class AuditOptions
{
    /// <summary>
    /// Configuration section name in appsettings.
    /// </summary>
    public const string SectionName = "Audit";
    
    /// <summary>
    /// Level of PII masking applied to audit log details.
    /// </summary>
    public PiiMaskingLevel PiiMaskingLevel { get; set; } = PiiMaskingLevel.Partial;
}

/// <summary>
/// Specifies the level of PII (Personally Identifiable Information) masking in audit logs.
/// </summary>
public enum PiiMaskingLevel
{
    /// <summary>
    /// No masking - PII is stored as-is.
    /// Use only in trusted, internal environments.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Partial masking - shows enough to identify, but protects full value.
    /// Example: "王*明", "j***@example.com"
    /// </summary>
    Partial = 1,
    
    /// <summary>
    /// Full masking - replaces all PII with "***".
    /// Most secure, for highly sensitive environments.
    /// </summary>
    Strict = 2
}

