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
}

/// <summary>
/// Request DTO for creating a new OIDC scope.
/// </summary>
public record CreateScopeRequest(
    string Name,
    string? DisplayName,
    string? Description,
    List<string>? Resources
);

/// <summary>
/// Request DTO for updating an existing OIDC scope.
/// </summary>
public record UpdateScopeRequest(
    string? Name,
    string? DisplayName,
    string? Description,
    List<string>? Resources
);
