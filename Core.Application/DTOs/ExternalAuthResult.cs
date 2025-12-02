namespace Core.Application.DTOs;

/// <summary>
/// External authentication result (AD, Google, Facebook, etc.)
/// </summary>
public class ExternalAuthResult
{
    /// <summary>
    /// Authentication provider name ("ActiveDirectory", "Google", "Facebook")
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    
    /// <summary>
    /// External system's unique user identifier
    /// Examples: AD username, Google user ID, Facebook user ID
    /// </summary>
    public string ProviderKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Email (primarily used to determine if it's the same Person)
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// First name / Given name
    /// </summary>
    public string? FirstName { get; set; }
    
    /// <summary>
    /// Last name / Family name
    /// </summary>
    public string? LastName { get; set; }
    
    /// <summary>
    /// Middle name
    /// </summary>
    public string? MiddleName { get; set; }
    
    /// <summary>
    /// Employee ID (AD may provide this)
    /// </summary>
    public string? EmployeeId { get; set; }
    
    /// <summary>
    /// Department
    /// </summary>
    public string? Department { get; set; }
    
    /// <summary>
    /// Job title
    /// </summary>
    public string? JobTitle { get; set; }
    
    /// <summary>
    /// Phone number
    /// </summary>
    public string? PhoneNumber { get; set; }
    
    /// <summary>
    /// Display name (used for AspNetUserLogins.ProviderDisplayKey)
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Identity document fields for Person matching
    /// </summary>
    public string? NationalId { get; set; }
    
    /// <summary>
    /// Passport number for Person matching
    /// </summary>
    public string? PassportNumber { get; set; }
    
    /// <summary>
    /// Resident certificate number for Person matching
    /// </summary>
    public string? ResidentCertificateNumber { get; set; }
    
    /// <summary>
    /// Additional claims (optional)
    /// </summary>
    public Dictionary<string, string>? AdditionalClaims { get; set; }
}
