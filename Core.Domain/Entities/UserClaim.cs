namespace Core.Domain.Entities;

/// <summary>
/// Represents a user claim definition that can be included in ID tokens and access tokens.
/// Maps OIDC standard claims and custom enterprise claims to ApplicationUser properties.
/// </summary>
public class UserClaim
{
    /// <summary>
    /// Unique identifier for the claim definition.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Internal name for the claim (e.g., "email", "given_name", "department").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Human-readable display name for admin UI (e.g., "Email Address", "Given Name").
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Description of what this claim represents and when it's used.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// JWT claim type (e.g., "email", "given_name", "department").
    /// This is the actual claim name that appears in tokens.
    /// </summary>
    public required string ClaimType { get; set; }

    /// <summary>
    /// Property path on ApplicationUser entity to source the claim value
    /// (e.g., "Email", "FirstName", "Department").
    /// Supports nested properties using dot notation (e.g., "Profile.Bio").
    /// </summary>
    public required string UserPropertyPath { get; set; }

    /// <summary>
    /// Data type of the claim value (String, Boolean, Integer, DateTime, JSON).
    /// </summary>
    public required string DataType { get; set; }

    /// <summary>
    /// Indicates if this is an OIDC standard claim (true) or custom enterprise claim (false).
    /// Standard claims cannot be deleted.
    /// </summary>
    public bool IsStandard { get; set; }

    /// <summary>
    /// If true, this claim will always be included in tokens when the user has a value for it
    /// (e.g., "sub" claim is always required).
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Navigation property: Scopes that include this claim when requested.
    /// </summary>
    public ICollection<ScopeClaim> ScopeClaims { get; set; } = new List<ScopeClaim>();
}
