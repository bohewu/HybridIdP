namespace Core.Domain.Entities;

/// <summary>
/// Represents a role assigned to a user for a specific client application.
/// This allows users to have different roles in different applications (e.g., "Admin" in App A, "User" in App B).
/// </summary>
public class UserAppRole
{
    public int Id { get; set; }

    public required Guid UserId { get; set; }

    /// <summary>
    /// The Client ID (OpenIddict Application ID) that this role applies to.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// The role name assigned to the user for this client.
    /// </summary>
    public required string RoleName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ApplicationUser? User { get; set; }
}
