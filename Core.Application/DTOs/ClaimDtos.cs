namespace Core.Application.DTOs;

/// <summary>
/// Claim definition DTO for user claim management.
/// </summary>
public sealed class ClaimDefinitionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ClaimType { get; set; } = string.Empty;
    public string UserPropertyPath { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsStandard { get; set; }
    public bool IsRequired { get; set; }
    public int ScopeCount { get; set; }
}

/// <summary>
/// Request DTO for creating a new claim definition.
/// </summary>
public record CreateClaimRequest(
    string Name,
    string? DisplayName,
    string? Description,
    string ClaimType,
    string? UserPropertyPath,
    string? DataType,
    bool? IsRequired
);

/// <summary>
/// Request DTO for updating an existing claim definition.
/// </summary>
public record UpdateClaimRequest(
    string? DisplayName,
    string? Description,
    string? ClaimType,
    string? UserPropertyPath,
    string? DataType,
    bool? IsRequired
);
