using System.Text.Json;
using System.Text.Json.Serialization;

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
    public string? ProviderProfileSource { get; set; }
    [JsonIgnore]
    public string? ConditionJson { get; set; }
    public ClaimCondition? Condition
    {
        get => ConditionJson is null ? null : JsonSerializer.Deserialize<ClaimCondition>(ConditionJson);
        set => ConditionJson = value is null ? null : JsonSerializer.Serialize(value);
    }
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
    bool? IsRequired,
    string? ProviderProfileSource = null,
    ClaimCondition? Condition = null
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
    bool? IsRequired,
    string? ProviderProfileSource = null,
    ClaimCondition? Condition = null
);
