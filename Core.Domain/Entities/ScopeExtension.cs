namespace Core.Domain.Entities;

/// <summary>
/// Extended properties for OpenIddict scopes to support consent screen customization.
/// References OpenIddict scope by ScopeId (Guid).
/// </summary>
public class ScopeExtension
{
    /// <summary>
    /// Primary key (auto-increment)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Reference to OpenIddict scope Id (from OpenIddictScopes table)
    /// </summary>
    public required string ScopeId { get; set; }

    /// <summary>
    /// Localized display name for consent screen (e.g., "Access your email")
    /// Falls back to OpenIddict DisplayName if null
    /// </summary>
    public string? ConsentDisplayName { get; set; }

    /// <summary>
    /// Detailed description of what this permission allows (for consent screen)
    /// </summary>
    public string? ConsentDescription { get; set; }

    /// <summary>
    /// Optional icon URL or icon class (e.g., "bi bi-envelope" for Bootstrap Icons)
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Whether this scope is required and cannot be opted out by the user
    /// (e.g., openid is usually required for OIDC)
    /// </summary>
    public bool IsRequired { get; set; } = false;

    /// <summary>
    /// Display order on consent screen (lower numbers appear first)
    /// </summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Category for grouping on consent screen (e.g., "Profile", "API Access", "Custom")
    /// </summary>
    public string? Category { get; set; }
}
