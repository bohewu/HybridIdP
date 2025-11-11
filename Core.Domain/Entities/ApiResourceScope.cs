namespace Core.Domain.Entities;

/// <summary>
/// Join entity representing the many-to-many relationship between API Resources and Scopes.
/// A scope can belong to one API resource, and an API resource can have many scopes.
/// </summary>
public class ApiResourceScope
{
    /// <summary>
    /// Primary key (auto-increment)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to ApiResource
    /// </summary>
    public int ApiResourceId { get; set; }

    /// <summary>
    /// Reference to OpenIddict scope Id (from OpenIddictScopes table)
    /// </summary>
    public required string ScopeId { get; set; }

    /// <summary>
    /// Navigation property to the API resource
    /// </summary>
    public virtual ApiResource? ApiResource { get; set; }
}
