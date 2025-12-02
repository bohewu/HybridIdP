namespace Core.Application.DTOs;

/// <summary>
/// DTO for creating or updating a Person
/// Phase 10.2: Person Service & API
/// Phase 10.6: Added identity verification fields
/// </summary>
public class PersonDto
{
    // Contact Information
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    
    // Name Information
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Nickname { get; set; }
    
    // Employment Information
    public string? EmployeeId { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    
    // Profile Information
    public string? ProfileUrl { get; set; }
    public string? PictureUrl { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }  // JSON string
    public string? Birthdate { get; set; }  // ISO 8601 format (YYYY-MM-DD)
    public string? Gender { get; set; }
    public string? TimeZone { get; set; }
    public string? Locale { get; set; }
    
    // Phase 10.6: Identity Verification Fields
    public string? NationalId { get; set; }
    public string? PassportNumber { get; set; }
    public string? ResidentCertificateNumber { get; set; }
    public string? IdentityDocumentType { get; set; }
}

/// <summary>
/// DTO for Person response with linked accounts
/// </summary>
public class PersonResponseDto : PersonDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public List<LinkedAccountDto>? Accounts { get; set; }
    
    // Phase 10.6: Identity Verification Audit Fields
    public DateTime? IdentityVerifiedAt { get; set; }
    public Guid? IdentityVerifiedBy { get; set; }
}

/// <summary>
/// DTO for linked account information
/// Phase 11.2: Extended with Roles and IsCurrentAccount for account management
/// </summary>
public class LinkedAccountDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; } // Phase 11.2: Added for clarity (same as Id)
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public List<string> Roles { get; set; } = new(); // Phase 11.2: User's assigned roles
    public bool IsCurrentAccount { get; set; } // Phase 11.2: Indicates if this is the current account
}

/// <summary>
/// DTO for linking an account to a person
/// </summary>
public class LinkAccountDto
{
    public Guid UserId { get; set; }
}

/// <summary>
/// DTO for paginated Person list response
/// </summary>
public class PersonListResponseDto
{
    public List<PersonResponseDto> Persons { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

/// <summary>
/// DTO for available role information
/// Phase 11.2: Account Management - Role Switching
/// </summary>
public class AvailableRoleDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } // True if this is the currently active role in the session
    public bool RequiresPasswordConfirmation { get; set; } // True for Admin and other sensitive roles
}
