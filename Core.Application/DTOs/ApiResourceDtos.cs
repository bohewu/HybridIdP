using System.ComponentModel.DataAnnotations;

namespace Core.Application.DTOs;

/// <summary>
/// Summary of an API Resource for list displays.
/// </summary>
public sealed class ApiResourceSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public int ScopeCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Detailed information about an API Resource, including associated scopes.
/// </summary>
public sealed class ApiResourceDetail
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<ResourceScopeInfo> Scopes { get; set; } = new();
}

/// <summary>
/// Information about a scope associated with an API Resource.
/// </summary>
public sealed class ResourceScopeInfo
{
    public string ScopeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Request model for creating a new API Resource.
/// </summary>
public record CreateApiResourceRequest(
    [Required(ErrorMessage = "Name is required")]
    [StringLength(200, ErrorMessage = "Name cannot exceed 200 characters")]
    string Name,
    
    [StringLength(200, ErrorMessage = "DisplayName cannot exceed 200 characters")]
    string? DisplayName,
    
    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    string? Description,
    
    [StringLength(500, ErrorMessage = "BaseUrl cannot exceed 500 characters")]
    [Url(ErrorMessage = "BaseUrl must be a valid URL")]
    string? BaseUrl,
    
    List<string>? ScopeIds
);

/// <summary>
/// Request model for updating an existing API Resource.
/// </summary>
public record UpdateApiResourceRequest(
    [Required(ErrorMessage = "Name is required")]
    [StringLength(200, ErrorMessage = "Name cannot exceed 200 characters")]
    string Name,
    
    [StringLength(200, ErrorMessage = "DisplayName cannot exceed 200 characters")]
    string? DisplayName,
    
    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    string? Description,
    
    [StringLength(500, ErrorMessage = "BaseUrl cannot exceed 500 characters")]
    [Url(ErrorMessage = "BaseUrl must be a valid URL")]
    string? BaseUrl,
    
    List<string>? ScopeIds
);
