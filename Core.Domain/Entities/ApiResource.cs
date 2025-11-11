namespace Core.Domain.Entities;

/// <summary>
/// Represents an API resource that can be protected by the Identity Provider.
/// API resources group related scopes (e.g., "Company API" with scopes like api:company:read, api:company:write).
/// When scopes are associated with API resources, access tokens will include audience claims.
/// </summary>
public class ApiResource
{
    /// <summary>
    /// Primary key (auto-increment)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Unique identifier for the API resource (e.g., "company_api", "inventory_api")
    /// Used in audience claims and resource identification
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Human-readable display name (e.g., "Company Management API")
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Detailed description of the API resource purpose
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Base URL of the API for documentation purposes (e.g., "https://api.company.com")
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Timestamp when the resource was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the resource was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property to scopes associated with this API resource
    /// </summary>
    public virtual ICollection<ApiResourceScope> Scopes { get; set; } = new List<ApiResourceScope>();
}
