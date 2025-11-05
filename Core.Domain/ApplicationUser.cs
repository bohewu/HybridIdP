using Microsoft.AspNetCore.Identity;

namespace Core.Domain;

public class ApplicationUser : IdentityUser<Guid>
{
    // Profile Information
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MiddleName { get; set; }
    public string? Nickname { get; set; }
    
    // Contact Information (PhoneNumber inherited from IdentityUser)
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    
    // Extended Profile (OIDC Standard Claims)
    public string? ProfileUrl { get; set; }
    public string? PictureUrl { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }  // JSON string
    public string? Birthdate { get; set; }  // ISO 8601 format (YYYY-MM-DD)
    public string? Gender { get; set; }
    public string? TimeZone { get; set; }
    public string? Locale { get; set; }
    
    // Enterprise Claims
    public string? EmployeeId { get; set; }
    
    // Account Status
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginDate { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Soft Delete
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    
    // Audit Fields
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

