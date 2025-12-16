using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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
    
    // Person Link (Phase 10.1: Multi-Account Identity)
    /// <summary>
    /// Foreign key to Person entity. Multiple ApplicationUsers can share the same PersonId.
    /// This allows a single real-life identity to have multiple authentication accounts.
    /// </summary>
    public Guid? PersonId { get; set; }
    
    /// <summary>
    /// Navigation property to Person entity
    /// </summary>
    public Entities.Person? Person { get; set; }
    
    // Account Status
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginDate { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Password Policy Fields
    public string PasswordHistory { get; set; } = "[]"; // Stores JSON array of hashed passwords
    public DateTime? LastPasswordChangeDate { get; set; }
    
    // TOTP Replay Attack Prevention (Phase 20.1)
    /// <summary>
    /// Stores the TOTP time window (Unix timestamp / 30) of the last successful validation.
    /// Used to prevent replay attacks by rejecting the same code in the same 30-second window.
    /// </summary>
    public long? LastTotpValidatedWindow { get; set; }
    
    // Email MFA Fields (Phase 20.3)
    /// <summary>
    /// Indicates whether Email-based MFA is enabled for this user.
    /// </summary>
    public bool EmailMfaEnabled { get; set; } = false;
    
    /// <summary>
    /// Hashed 6-digit code sent via email. Null when no code is pending.
    /// </summary>
    [MaxLength(128)]
    public string? EmailMfaCode { get; set; }
    
    /// <summary>
    /// Expiration time for the email MFA code. Code is invalid after this time.
    /// </summary>
    public DateTime? EmailMfaCodeExpiry { get; set; }
    
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

