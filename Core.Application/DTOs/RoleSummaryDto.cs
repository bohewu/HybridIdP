namespace Core.Application.DTOs;

/// <summary>
/// Summary information for a role (list view)
/// </summary>
public class RoleSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int UserCount { get; set; }
    public List<string> Permissions { get; set; } = new();
    public bool IsSystem { get; set; }
}
