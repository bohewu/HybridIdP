namespace Core.Domain.Entities;

/// <summary>
/// Represents a client-specific required scope relationship.
/// Required scopes are non-optional during consent and must be granted.
/// This is separate from the global ScopeExtension.IsRequired flag,
/// allowing per-client required scope configuration.
/// </summary>
public class ClientRequiredScope
{
    /// <summary>
    /// Primary key (auto-increment)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to OpenIddict application (Client ID as GUID string)
    /// References OpenIddictApplications.Id
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Reference to OpenIddict scope Id (from OpenIddictScopes table)
    /// This is the scope's GUID identifier, not the scope name
    /// </summary>
    public required string ScopeId { get; set; }

    /// <summary>
    /// Timestamp when the required scope was added
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who added this required scope (optional, can be admin user ID)
    /// </summary>
    public string? CreatedBy { get; set; }
}
