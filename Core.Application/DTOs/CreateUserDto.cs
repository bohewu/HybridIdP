using System.ComponentModel.DataAnnotations;
namespace Core.Application.DTOs;

/// <summary>
/// Request to create a new user
/// </summary>
public class CreateUserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string UserName { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
    
    // Profile Information
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? EmployeeId { get; set; }
    
    // Roles
    public List<string> Roles { get; set; } = new();
    
    // Account Settings
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
}
