namespace Core.Application.DTOs;

/// <summary>
/// Request to create a new role
/// </summary>
public class CreateRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
}
