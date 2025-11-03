using Microsoft.AspNetCore.Identity;

namespace Core.Domain;

/// <summary>
/// Extended role entity with additional fields
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>
    /// Description of the role's purpose and responsibilities
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Indicates if this is a system role that cannot be deleted (Admin, User)
    /// </summary>
    public bool IsSystem { get; set; }
    
    /// <summary>
    /// Comma-separated list of permissions assigned to this role
    /// Example: "clients.read,clients.create,users.read"
    /// </summary>
    public string? Permissions { get; set; }
    
    /// <summary>
    /// When the role was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the role was last modified
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}
