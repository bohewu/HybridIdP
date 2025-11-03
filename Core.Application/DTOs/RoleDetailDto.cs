namespace Core.Application.DTOs;

/// <summary>
/// Detailed role information
/// </summary>
public class RoleDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NormalizedName { get; set; }
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public bool IsSystem { get; set; }
    public int UserCount { get; set; }
    public List<UserSummaryDto> Users { get; set; } = new();
}
