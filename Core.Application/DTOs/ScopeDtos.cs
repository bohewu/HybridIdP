namespace Core.Application.DTOs;

/// <summary>
/// Summary DTO for OIDC scope listing.
/// </summary>
public sealed class ScopeSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string> Resources { get; set; } = new();
    
    // Consent screen customization fields
    public string? ConsentDisplayNameKey { get; set; }
    public string? ConsentDescriptionKey { get; set; }
    public string? IconUrl { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public string? Category { get; set; }
    public bool IsPublic { get; set; }
}

/// <summary>
/// Request DTO for creating a new OIDC scope.
/// </summary>
public record CreateScopeRequest(
    string Name,
    string? DisplayName,
    string? Description,
    List<string>? Resources,
    // Consent screen customization
    string? ConsentDisplayNameKey = null,
    string? ConsentDescriptionKey = null,
    string? IconUrl = null,
    bool IsRequired = false,
    int DisplayOrder = 0,
    string? Category = null,
    bool IsPublic = false
);

/// <summary>
/// Request DTO for updating an existing OIDC scope.
/// </summary>
public record UpdateScopeRequest(
    string? Name,
    string? DisplayName,
    string? Description,
    List<string>? Resources,
    // Consent screen customization
    string? ConsentDisplayNameKey = null,
    string? ConsentDescriptionKey = null,
    string? IconUrl = null,
    bool? IsRequired = null,
    int? DisplayOrder = null,
    string? Category = null,
    bool? IsPublic = null
);
