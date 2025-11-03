namespace Core.Application.DTOs;

/// <summary>
/// Request to update an existing role
/// </summary>
public class UpdateRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
}
