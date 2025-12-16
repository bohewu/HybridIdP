namespace Core.Application.DTOs;

/// <summary>
/// Summary view of a user for list displays
/// </summary>
public class UserSummaryDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public List<string> Roles { get; set; } = new();
}
