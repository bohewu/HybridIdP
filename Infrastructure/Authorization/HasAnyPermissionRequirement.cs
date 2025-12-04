using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Authorization;

/// <summary>
/// Requirement that checks if user has ANY of the specified permissions
/// Used for shared admin pages that can be accessed by different admin-level roles
/// </summary>
public class HasAnyPermissionRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; }

    public HasAnyPermissionRequirement(params string[] permissions)
    {
        Permissions = permissions.ToList().AsReadOnly();
    }
}
