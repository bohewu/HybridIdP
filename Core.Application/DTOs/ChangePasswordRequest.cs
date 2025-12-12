using System.ComponentModel.DataAnnotations;

namespace Core.Application.DTOs;

/// <summary>
/// Request DTO for changing user password
/// </summary>
public class ChangePasswordRequest
{
    /// <summary>
    /// Current password (for verification)
    /// </summary>
    [Required(ErrorMessage = "Current password is required")]
    public string CurrentPassword { get; set; } = string.Empty;
    
    /// <summary>
    /// New password (will be validated by DynamicPasswordValidator)
    /// </summary>
    [Required(ErrorMessage = "New password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    public string NewPassword { get; set; } = string.Empty;
    
    /// <summary>
    /// Confirmation of new password
    /// </summary>
    [Required(ErrorMessage = "Password confirmation is required")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
