using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Authorization;

/// <summary>
/// Requirement for permission-based authorization
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
