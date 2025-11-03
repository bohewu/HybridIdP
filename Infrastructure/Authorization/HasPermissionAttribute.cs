using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Authorization;

/// <summary>
/// Attribute to require a specific permission for an endpoint
/// </summary>
public class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission)
    {
        Policy = permission;
    }
}
