namespace Core.Application.DTOs;

/// <summary>
/// DTO representing a claim associated with a specific scope.
/// </summary>
public sealed class ScopeClaimDto
{
    public int Id { get; set; }
    public string ScopeId { get; set; } = string.Empty;
    public string ScopeName { get; set; } = string.Empty;
    public int ClaimId { get; set; }
    public string ClaimName { get; set; } = string.Empty;
    public string ClaimDisplayName { get; set; } = string.Empty;
    public string ClaimType { get; set; } = string.Empty;
    public bool AlwaysInclude { get; set; }
    public string? CustomMappingLogic { get; set; }
}

/// <summary>
/// Request DTO for updating the claims associated with a scope.
/// </summary>
public record UpdateScopeClaimsRequest(
    List<int>? ClaimIds
);
