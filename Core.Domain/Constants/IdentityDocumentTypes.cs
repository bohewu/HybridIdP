namespace Core.Domain.Constants;

/// <summary>
/// Identity document types for Person identity verification (Phase 10.6).
/// Used in Person.IdentityDocumentType field.
/// </summary>
public static class IdentityDocumentTypes
{
    /// <summary>
    /// National ID / 身分證字號 (Taiwan ROC ID format)
    /// </summary>
    public const string NationalId = "NationalId";
    
    /// <summary>
    /// Passport / 護照號碼
    /// </summary>
    public const string Passport = "Passport";
    
    /// <summary>
    /// Resident Certificate / 居留證號碼 (for foreign residents)
    /// </summary>
    public const string ResidentCertificate = "ResidentCertificate";
    
    /// <summary>
    /// No identity document provided
    /// </summary>
    public const string None = "None";
}
