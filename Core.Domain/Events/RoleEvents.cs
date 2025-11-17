using Core.Domain.Events;

namespace Core.Domain.Events;

/// <summary>
/// Event raised when a role is created.
/// </summary>
public class RoleCreatedEvent : IDomainEvent
{
    public string RoleId { get; }
    public string RoleName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public RoleCreatedEvent(string roleId, string roleName)
    {
        RoleId = roleId;
        RoleName = roleName;
    }
}

/// <summary>
/// Event raised when a role is updated.
/// </summary>
public class RoleUpdatedEvent : IDomainEvent
{
    public string RoleId { get; }
    public string RoleName { get; }
    public string Changes { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public RoleUpdatedEvent(string roleId, string roleName, string changes)
    {
        RoleId = roleId;
        RoleName = roleName;
        Changes = changes;
    }
}

/// <summary>
/// Event raised when a role is deleted.
/// </summary>
public class RoleDeletedEvent : IDomainEvent
{
    public string RoleId { get; }
    public string RoleName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public RoleDeletedEvent(string roleId, string roleName)
    {
        RoleId = roleId;
        RoleName = roleName;
    }
}

/// <summary>
/// Event raised when a role's permissions are changed.
/// </summary>
public class RolePermissionChangedEvent : IDomainEvent
{
    public string RoleId { get; }
    public string RoleName { get; }
    public string PermissionChanges { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public RolePermissionChangedEvent(string roleId, string roleName, string permissionChanges)
    {
        RoleId = roleId;
        RoleName = roleName;
        PermissionChanges = permissionChanges;
    }
}