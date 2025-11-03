namespace Core.Application.DTOs;

/// <summary>
/// Detailed user information for edit/view
/// </summary>
public class UserDetailDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? UserName { get; set; }
    
    // Profile Information
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MiddleName { get; set; }
    public string? Nickname { get; set; }
    
    // Contact Information
    public string? PhoneNumber { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    
    // Extended Profile
    public string? ProfileUrl { get; set; }
    public string? PictureUrl { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Birthdate { get; set; }
    public string? Gender { get; set; }
    public string? TimeZone { get; set; }
    public string? Locale { get; set; }
    
    // Enterprise Claims
    public string? EmployeeId { get; set; }
    
    // Account Status
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTime? LastLoginDate { get; set; }
    
    // Audit
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    
    // Roles
    public List<string> Roles { get; set; } = new();
}
