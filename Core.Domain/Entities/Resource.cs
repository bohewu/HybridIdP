namespace Core.Domain.Entities;

/// <summary>
/// Localization resources for multi-language support.
/// Stores translated strings for various UI elements including consent screen descriptions.
/// </summary>
public class Resource
{
    /// <summary>
    /// Primary key (auto-increment)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Resource key (e.g., "Scope.Email.ConsentDisplayName", "Scope.Profile.ConsentDescription")
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Culture code (e.g., "en-US", "zh-TW")
    /// </summary>
    public required string Culture { get; set; }

    /// <summary>
    /// Translated value
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Optional category for organizing resources (e.g., "Scope", "Error", "UI")
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Date created (UTC)
    /// </summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date last updated (UTC)
    /// </summary>
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
