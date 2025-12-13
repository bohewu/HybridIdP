using System;
using System.Collections.Generic;

namespace Core.Application.DTOs;

/// <summary>
/// DTO for user profile information returned to the client
/// </summary>
public class ProfileDto
{
    // ApplicationUser data (read-only)
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    
    // User Preferences (ApplicationUser)
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }
    
    // Person data (partially editable)
    public PersonProfileDto? Person { get; set; }
    
    // External logins (read-only)
    public List<ExternalLoginDto> ExternalLogins { get; set; } = new();
    
    // Password management flags
    public bool HasLocalPassword { get; set; }  // False for external login accounts
    public bool AllowPasswordChange { get; set; }  // Based on SecurityPolicy
}

/// <summary>
/// DTO for Person information in profile context
/// </summary>
public class PersonProfileDto
{
    public Guid PersonId { get; set; }
    public string FullName { get; set; } = string.Empty;
    
    // Read-only fields
    public string? EmployeeId { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    
    // Editable fields
    public string? PhoneNumber { get; set; }
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }
}

/// <summary>
/// DTO for external login information
/// </summary>
public class ExternalLoginDto
{
    public string LoginProvider { get; set; } = string.Empty;
    public string? ProviderDisplayName { get; set; }
}
