# Phase 10.6: Person Identity Verification & Uniqueness

**Status:** 🔵 PLANNING  
**Priority:** HIGH  
**Estimated Effort:** 3-4 days

## Overview

Phase 10.6 addresses critical issues with Person entity uniqueness and identity verification. Currently, there's no mechanism to prevent duplicate Person records for the same real-world individual, which could lead to data integrity issues and security concerns.

## Problem Statement

### 1. Person Uniqueness Issue
- **Current State:** Person entity has no unique identity field
- **Risk:** Multiple Person records can be created for the same individual
- **Example Scenario:**
  - User registers with email `john@example.com` → Person A created
  - Later, same user registers with email `john.doe@example.com` → Person B created
  - Same person now has 2 separate Person entities, causing data fragmentation

### 2. Identity Verification Gap
- No government-issued ID validation
- No way to verify Person identity across different accounts
- Compliance risk for systems requiring KYC (Know Your Customer)

### 3. E2E Test Issues
- Phase 10.4 auto-creates Person on user creation
- Cannot test multi-account scenarios properly
- Need better control over Person-User linking behavior

## Proposed Solution

### Architecture: National ID / Passport as Unique Identifier

**Recommended Approach:** Add identity document fields with uniqueness constraints

```csharp
public class Person
{
    // Existing fields...
    
    // Identity Verification (Phase 10.6)
    
    /// <summary>
    /// National ID number (身分證字號) - Taiwan ROC ID format
    /// </summary>
    [MaxLength(20)]
    public string? NationalId { get; set; }
    
    /// <summary>
    /// Passport number (護照號碼)
    /// </summary>
    [MaxLength(20)]
    public string? PassportNumber { get; set; }
    
    /// <summary>
    /// Resident Certificate number (居留證號碼) - for foreign residents
    /// </summary>
    [MaxLength(20)]
    public string? ResidentCertificateNumber { get; set; }
    
    /// <summary>
    /// Identity document type (NationalId, Passport, ResidentCertificate)
    /// </summary>
    [MaxLength(30)]
    public string? IdentityDocumentType { get; set; }
    
    /// <summary>
    /// Date when identity was verified (null if not verified)
    /// </summary>
    public DateTime? IdentityVerifiedAt { get; set; }
    
    /// <summary>
    /// User ID who verified the identity (admin/verifier)
    /// </summary>
    public Guid? IdentityVerifiedBy { get; set; }
}
```

### Database Constraints

```csharp
// In ApplicationDbContext.OnModelCreating
modelBuilder.Entity<Person>(entity =>
{
    // Existing configurations...
    
    // Phase 10.6: Unique identity constraints
    entity.HasIndex(e => e.NationalId)
        .IsUnique()
        .HasFilter("[NationalId] IS NOT NULL");
        
    entity.HasIndex(e => e.PassportNumber)
        .IsUnique()
        .HasFilter("[PassportNumber] IS NOT NULL");
        
    entity.HasIndex(e => e.ResidentCertificateNumber)
        .IsUnique()
        .HasFilter("[ResidentCertificateNumber] IS NOT NULL");
});
```

**Why Filtered Unique Indexes?**
- Allows NULL values (not everyone needs to provide ID immediately)
- Prevents duplicate non-NULL values
- Supports gradual identity verification workflow

### Identity Document Type Enum

```csharp
namespace Core.Domain.Constants;

public static class IdentityDocumentTypes
{
    public const string NationalId = "NationalId";              // 身分證
    public const string Passport = "Passport";                   // 護照
    public const string ResidentCertificate = "ResidentCertificate"; // 居留證
    public const string None = "None";                          // 未驗證
}
```

## Implementation Tasks

### Task 1: Database Schema Update
- [ ] Add identity fields to Person entity
- [ ] Create EF Core migration for SQL Server
- [ ] Create EF Core migration for PostgreSQL
- [ ] Add filtered unique indexes
- [ ] Update seed data if needed

### Task 2: API & Service Layer
- [ ] Update `PersonDto` to include identity fields
- [ ] Update `CreatePersonDto` to include identity fields
- [ ] Update `UpdatePersonDto` to include identity fields
- [ ] Add validation for identity document formats (Taiwan ID, Passport)
- [ ] Update `PersonService.CreatePersonAsync` with duplicate check
- [ ] Update `PersonService.UpdatePersonAsync` with duplicate check
- [ ] Add `PersonService.VerifyIdentityAsync` method

### Task 3: Business Logic & Validation

#### Taiwan National ID Validation
```csharp
public static class NationalIdValidator
{
    // Taiwan ROC ID format: A123456789 (1 letter + 9 digits)
    public static bool IsValidTaiwanNationalId(string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(nationalId)) return false;
        if (nationalId.Length != 10) return false;
        
        // First character must be letter (A-Z)
        if (!char.IsLetter(nationalId[0])) return false;
        
        // Remaining 9 characters must be digits
        if (!nationalId.Substring(1).All(char.IsDigit)) return false;
        
        // TODO: Add checksum validation for Taiwan ID
        return true;
    }
}
```

#### Duplicate Detection Logic
```csharp
public async Task<(bool success, string? errorMessage)> CheckPersonUniquenessAsync(
    string? nationalId, 
    string? passportNumber, 
    string? residentCertificateNumber,
    Guid? excludePersonId = null)
{
    // Check if any identity document already exists
    var existingPerson = await _context.Persons
        .Where(p => excludePersonId == null || p.Id != excludePersonId)
        .Where(p => 
            (nationalId != null && p.NationalId == nationalId) ||
            (passportNumber != null && p.PassportNumber == passportNumber) ||
            (residentCertificateNumber != null && p.ResidentCertificateNumber == residentCertificateNumber))
        .FirstOrDefaultAsync();
        
    if (existingPerson != null)
    {
        return (false, $"Person with this identity document already exists (PersonId: {existingPerson.Id})");
    }
    
    return (true, null);
}
```

### Task 4: Admin UI Updates
- [ ] Add identity fields to Person create/edit forms
- [ ] Add identity document type dropdown
- [ ] Add validation messages for duplicate identity documents
- [ ] Add "Verify Identity" button/workflow in Person detail view
- [ ] Show identity verification status badge (Verified/Unverified)
- [ ] Add identity document search functionality

### Task 5: User Registration Flow Update
- [ ] Add optional national ID field to registration form
- [ ] Check for existing Person with same identity document
- [ ] Link new user to existing Person if identity document matches
- [ ] Show warning if Person already has linked accounts (security check)

### Task 6: E2E Test Fixes
- [ ] Update `multi-account-login.spec.ts` to handle Phase 10.4 auto-linking
- [ ] Add test for duplicate identity document rejection
- [ ] Add test for identity verification workflow
- [ ] Add test for linking user to existing Person by identity document

### Task 7: Audit & Security
- [ ] Log all identity document access (PII compliance)
- [ ] Log identity verification events
- [ ] Add permission check for viewing identity documents
- [ ] Consider masking identity documents in UI (show only last 4 digits)

### Task 8: Unit Tests
- [ ] Test NationalId validation logic
- [ ] Test duplicate detection logic
- [ ] Test identity verification workflow
- [ ] Test unique constraint violations
- [ ] Test filtered index behavior (NULL handling)

## Migration Strategy

### Existing Data Handling

**Option 1: Gradual Rollout (Recommended)**
```sql
-- Phase 10.6 Migration: All new fields are nullable
-- Existing Person records will have NULL identity documents
-- Identity can be added later through admin UI or user profile
```

**Option 2: Email-Based Uniqueness (Temporary)**
```csharp
// If no identity document provided, use Email as fallback uniqueness check
// This is NOT a permanent solution due to email change scenarios
```

### Transition Plan
1. **Phase 10.6.1:** Deploy with nullable identity fields
2. **Phase 10.6.2:** Admin UI to manually verify and add identity documents
3. **Phase 10.6.3:** Gradually require identity documents for new Person creation
4. **Phase 10.6.4:** (Future) Integration with government ID verification APIs

## Alternative Approaches Considered

### ❌ Email as Unique Identifier
**Rejected Reason:**
- Email can change over time
- Same person might use different emails for different accounts
- Not a reliable government-issued identifier

### ❌ Phone Number as Unique Identifier
**Rejected Reason:**
- Phone numbers can be recycled/reassigned
- Not universally available
- Not a legal identity document

### ❌ Biometric Data (Fingerprint, Face)
**Rejected Reason:**
- Complex infrastructure requirements
- Privacy concerns (GDPR/PIPEDA)
- Out of scope for Phase 10

### ✅ Government-Issued ID (Chosen Approach)
**Why This Works:**
- Legally binding identity proof
- Permanent and unique per individual
- Standard practice in KYC/AML compliance
- Supports multiple document types (flexibility)

## Configuration Options

```json
// appsettings.json - Phase 10.6 Settings
{
  "PersonIdentitySettings": {
    "RequireIdentityDocumentOnCreation": false,  // Start with optional
    "AllowedDocumentTypes": [
      "NationalId",
      "Passport", 
      "ResidentCertificate"
    ],
    "EnableAutomaticVerification": false,  // Manual verification initially
    "MaskIdentityDocumentsInUI": true,     // Show only last 4 digits
    "IdentityDocumentMaxAge": 3650,        // 10 years before re-verification
    "RequireVerificationForSensitiveOps": false  // Future: require for password reset, etc.
  }
}
```

## Security Considerations

### 1. Data Privacy
- Identity documents are **Personally Identifiable Information (PII)**
- Encrypt at rest (consider Azure Key Vault or similar)
- Mask in logs and UI
- Restrict access to administrators only

### 2. Compliance
- GDPR: Right to erasure (soft delete Person with identity documents)
- PIPEDA (Canada): Consent required before collecting identity documents
- Taiwan Personal Data Protection Act: Explicit consent and purpose limitation

### 3. Access Control
```csharp
public static class Permissions
{
    public static class Persons
    {
        // Existing permissions...
        public const string ViewIdentityDocuments = "Persons.ViewIdentityDocuments";
        public const string VerifyIdentity = "Persons.VerifyIdentity";
    }
}
```

## Testing Strategy

### Unit Tests
- Taiwan National ID format validation
- Passport number validation
- Duplicate detection logic
- Unique constraint enforcement

### Integration Tests
- Create Person with identity document
- Reject duplicate identity document
- Update identity document (with verification reset)
- Query Person by identity document

### E2E Tests
- Register user with national ID → links to existing Person
- Admin verifies Person identity via UI
- Duplicate identity document shows error in UI
- Multi-account login with identity-verified Person

## Success Metrics

- ✅ Zero duplicate Person records with same identity document
- ✅ All E2E tests passing (including multi-account-login)
- ✅ Identity verification workflow functional
- ✅ 100% unit test coverage for validation logic
- ✅ Admin UI supports identity document management

## Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| Users refuse to provide ID | Medium | Make identity documents optional initially |
| Identity document format varies by country | High | Start with Taiwan ID only, expand later |
| Data privacy violations | High | Implement encryption, access controls, audit logs |
| Performance impact (unique index queries) | Low | Filtered indexes + proper indexing strategy |
| Legacy data without identity documents | Medium | Gradual migration, NULL handling |

## Future Enhancements (Post Phase 10.6)

- **Phase 10.7:** Integration with government ID verification APIs (Taiwan MOI)
- **Phase 10.8:** OCR for ID document scanning
- **Phase 10.9:** Multi-region support (US SSN, Canada SIN, etc.)
- **Phase 10.10:** Blockchain-based identity verification

## Dependencies

- Phase 10.1-10.5 must be complete
- EF Core migrations infrastructure
- Admin UI with Person management
- Audit logging system

## Estimated Timeline

- Day 1: Database schema + migrations + entity updates
- Day 2: Service layer + validation logic + duplicate detection
- Day 3: Admin UI + identity verification workflow
- Day 4: E2E tests + unit tests + documentation

## References

- [Taiwan National ID Format](https://en.wikipedia.org/wiki/National_identification_number#Taiwan)
- [Passport Standards (ICAO)](https://www.icao.int/Security/FAL/TRIP/Pages/Publications.aspx)
- [GDPR Article 4 - Personal Data](https://gdpr-info.eu/art-4-gdpr/)
- [EF Core Filtered Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-filter)

---

**Next Steps:**
1. Review and approve this design document
2. Start implementation with Task 1 (Database Schema)
3. Iterative development with frequent testing
4. Deploy to staging for validation before production
