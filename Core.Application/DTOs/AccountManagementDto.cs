namespace Core.Application.DTOs;

/// <summary>
/// Represents a linked account for the current user's Person entity
/// Phase 11.2: Account Management
/// </summary>
public class LinkedAccountDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsCurrentAccount { get; set; }
}

/// <summary>
/// Represents an available role that the user can switch to
/// Phase 11.2: Account Management
/// </summary>
public class AvailableRoleDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    
    /// <summary>
    /// Indicates if switching to this role requires password confirmation (e.g., Admin role)
    /// </summary>
    public bool RequiresPasswordConfirmation { get; set; }
}
