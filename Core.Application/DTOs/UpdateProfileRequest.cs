using System.ComponentModel.DataAnnotations;

namespace Core.Application.DTOs;

/// <summary>
/// Request DTO for updating user profile (Person table fields)
/// </summary>
public class UpdateProfileRequest
{
    /// <summary>
    /// Phone number (updates Person.PhoneNumber)
    /// </summary>
    /// <summary>
    /// Phone number (updates Person.PhoneNumber)
    /// </summary>
    [MaxLength(50)]
    public string? PhoneNumber { get; set; }
    
    /// <summary>
    /// Preferred locale (e.g., "zh-TW", "en-US")
    /// </summary>
    [MaxLength(10)]
    public string? Locale { get; set; }
    
    /// <summary>
    /// Time zone (e.g., "Asia/Taipei")
    /// </summary>
    [MaxLength(50)]
    public string? TimeZone { get; set; }
}
