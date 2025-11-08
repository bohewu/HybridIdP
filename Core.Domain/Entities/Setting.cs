using System.ComponentModel.DataAnnotations;
using Core.Domain.Constants;

namespace Core.Domain.Entities;

/// <summary>
/// System-wide key/value setting with optional data type metadata and auditing.
/// </summary>
public class Setting
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Unique key, e.g., 'branding.appName' or 'security.password.minLength'
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Key { get; set; } = default!;

    /// <summary>
    /// Raw value serialized as string; consumers may parse to target types.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Optional data type hint (stored as string in DB via EF conversion).
    /// </summary>
    public SettingDataType DataType { get; set; } = SettingDataType.String;

    /// <summary>
    /// UTC timestamp of the last update; used for cache invalidation.
    /// </summary>
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Identifier or name of the admin who made the last update.
    /// </summary>
    [MaxLength(200)]
    public string? UpdatedBy { get; set; }
}