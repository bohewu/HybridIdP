using System;
using Core.Domain.Enums;

namespace Core.Domain.Entities;

/// <summary>
/// Represents a real-life identity (person/employee) that can have multiple authentication accounts.
/// This entity centralizes profile information and employment history.
/// Phase 18: Added lifecycle management fields (Status, StartDate, EndDate, soft delete)
/// </summary>
public class Person
{
    /// <summary>
    /// Unique identifier for the person
    /// </summary>
    public Guid Id { get; set; }
    
    // Contact Information
    /// <summary>
    /// Primary email address for this person (can be manually set by admin)
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Phone number
    /// </summary>
    public string? PhoneNumber { get; set; }
    
    // Profile Information
    /// <summary>
    /// First name / Given name
    /// </summary>
    public string? FirstName { get; set; }
    
    /// <summary>
    /// Middle name
    /// </summary>
    public string? MiddleName { get; set; }
    
    /// <summary>
    /// Last name / Family name / Surname
    /// </summary>
    public string? LastName { get; set; }
    
    /// <summary>
    /// Preferred name / Nickname
    /// </summary>
    public string? Nickname { get; set; }
    
    // Enterprise Information
    /// <summary>
    /// Employee ID / Staff ID (unique identifier in organization)
    /// </summary>
    public string? EmployeeId { get; set; }
    
    /// <summary>
    /// Department or organizational unit
    /// </summary>
    public string? Department { get; set; }
    
    /// <summary>
    /// Job title / Position
    /// </summary>
    public string? JobTitle { get; set; }
    
    // Extended Profile (OIDC Standard Claims)
    /// <summary>
    /// URL to user's profile page
    /// </summary>
    public string? ProfileUrl { get; set; }
    
    /// <summary>
    /// URL to user's profile picture
    /// </summary>
    public string? PictureUrl { get; set; }
    
    /// <summary>
    /// User's website URL
    /// </summary>
    public string? Website { get; set; }
    
    /// <summary>
    /// Physical address (stored as JSON string)
    /// </summary>
    public string? Address { get; set; }
    
    /// <summary>
    /// Date of birth in ISO 8601 format (YYYY-MM-DD)
    /// </summary>
    public string? Birthdate { get; set; }
    
    /// <summary>
    /// Gender identity
    /// </summary>
    public string? Gender { get; set; }
    
    /// <summary>
    /// Time zone (e.g., "America/New_York")
    /// </summary>
    public string? TimeZone { get; set; }
    
    /// <summary>
    /// Preferred locale/language (e.g., "en-US", "zh-TW")
    /// </summary>
    public string? Locale { get; set; }
    
    // Identity Verification (Phase 10.6)
    /// <summary>
    /// National ID number (身分證字號) - Taiwan ROC ID format
    /// </summary>
    public string? NationalId { get; set; }
    
    /// <summary>
    /// Passport number (護照號碼)
    /// </summary>
    public string? PassportNumber { get; set; }
    
    /// <summary>
    /// Resident Certificate number (居留證號碼) - for foreign residents
    /// </summary>
    public string? ResidentCertificateNumber { get; set; }
    
    /// <summary>
    /// Identity document type (NationalId, Passport, ResidentCertificate, None)
    /// </summary>
    public string? IdentityDocumentType { get; set; }
    
    /// <summary>
    /// Date when identity was verified (null if not verified)
    /// </summary>
    public DateTime? IdentityVerifiedAt { get; set; }
    
    /// <summary>
    /// User ID who verified the identity (admin/verifier)
    /// </summary>
    public Guid? IdentityVerifiedBy { get; set; }
    
    // Lifecycle Management (Phase 18)
    /// <summary>
    /// Current lifecycle status of the person (Pending, Active, Suspended, Resigned, Terminated)
    /// </summary>
    public PersonStatus Status { get; set; } = PersonStatus.Active;
    
    /// <summary>
    /// Employment start date (inclusive). Person becomes active on or after this date.
    /// </summary>
    public DateTime? StartDate { get; set; }
    
    /// <summary>
    /// Employment end date (inclusive). This is the last working day.
    /// Person becomes inactive the day after this date.
    /// </summary>
    public DateTime? EndDate { get; set; }
    
    // Soft Delete
    /// <summary>
    /// Whether this person record is soft-deleted
    /// </summary>
    public bool IsDeleted { get; set; }
    
    /// <summary>
    /// Timestamp when this person record was soft-deleted
    /// </summary>
    public DateTime? DeletedAt { get; set; }
    
    /// <summary>
    /// User ID who soft-deleted this person record
    /// </summary>
    public Guid? DeletedBy { get; set; }
    
    // Audit Fields
    /// <summary>
    /// User ID who created this person record
    /// </summary>
    public Guid? CreatedBy { get; set; }
    
    /// <summary>
    /// Timestamp when this person record was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// User ID who last modified this person record
    /// </summary>
    public Guid? ModifiedBy { get; set; }
    
    /// <summary>
    /// Timestamp when this person record was last modified
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
    
    // Navigation Properties
    /// <summary>
    /// Collection of authentication accounts (ApplicationUser) linked to this person
    /// </summary>
    public ICollection<ApplicationUser>? Accounts { get; set; }
    
    // Helper Methods (Phase 18)
    /// <summary>
    /// Checks if the person is currently allowed to authenticate based on status and dates.
    /// </summary>
    public bool CanAuthenticate()
    {
        if (IsDeleted) return false;
        if (Status != PersonStatus.Active) return false;
        
        var now = DateTime.UtcNow.Date;
        if (StartDate.HasValue && StartDate.Value.Date > now) return false;
        if (EndDate.HasValue && EndDate.Value.Date < now) return false;
        
        return true;
    }
}

