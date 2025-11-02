namespace Core.Domain.Entities;

/// <summary>
/// Join table that maps OIDC scopes to user claims.
/// Defines which claims are included in ID tokens when a specific scope is requested.
/// Example: "profile" scope → "name", "given_name", "family_name" claims
/// </summary>
public class ScopeClaim
{
    /// <summary>
    /// Unique identifier for the scope-claim mapping.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key: ID of the OIDC scope (from OpenIddict's scope store).
    /// This is a string because OpenIddict uses string IDs for scopes.
    /// </summary>
    public required string ScopeId { get; set; }

    /// <summary>
    /// Name of the scope (e.g., "profile", "email", "api:read").
    /// Denormalized for query performance.
    /// </summary>
    public required string ScopeName { get; set; }

    /// <summary>
    /// Foreign key: ID of the user claim definition.
    /// </summary>
    public int UserClaimId { get; set; }

    /// <summary>
    /// Navigation property to the claim definition.
    /// </summary>
    public UserClaim? UserClaim { get; set; }

    /// <summary>
    /// Indicates if this claim should always be included when the scope is granted.
    /// If false, the claim is only included if the user has a non-null value for it.
    /// </summary>
    public bool AlwaysInclude { get; set; }

    /// <summary>
    /// Custom mapping logic override (optional).
    /// If specified, this expression is used instead of UserClaim.UserPropertyPath.
    /// Example: "string.Join(' ', FirstName, LastName)" for "name" claim.
    /// </summary>
    public string? CustomMappingLogic { get; set; }
}
