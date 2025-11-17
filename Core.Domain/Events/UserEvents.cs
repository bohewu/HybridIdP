using Core.Domain.Events;

namespace Core.Domain.Events;

/// <summary>
/// Event raised when a user is created.
/// </summary>
public class UserCreatedEvent : IDomainEvent
{
    public string UserId { get; }
    public string UserName { get; }
    public string Email { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public UserCreatedEvent(string userId, string userName, string email)
    {
        UserId = userId;
        UserName = userName;
        Email = email;
    }
}

/// <summary>
/// Event raised when a user is updated.
/// </summary>
public class UserUpdatedEvent : IDomainEvent
{
    public string UserId { get; }
    public string UserName { get; }
    public string Changes { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public UserUpdatedEvent(string userId, string userName, string changes)
    {
        UserId = userId;
        UserName = userName;
        Changes = changes;
    }
}

/// <summary>
/// Event raised when a user is deleted.
/// </summary>
public class UserDeletedEvent : IDomainEvent
{
    public string UserId { get; }
    public string UserName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public UserDeletedEvent(string userId, string userName)
    {
        UserId = userId;
        UserName = userName;
    }
}

/// <summary>
/// Event raised when a user's role is assigned or changed.
/// </summary>
public class UserRoleAssignedEvent : IDomainEvent
{
    public string UserId { get; }
    public string UserName { get; }
    public string RoleName { get; }
    public bool IsAssigned { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public UserRoleAssignedEvent(string userId, string userName, string roleName, bool isAssigned)
    {
        UserId = userId;
        UserName = userName;
        RoleName = roleName;
        IsAssigned = isAssigned;
    }
}

/// <summary>
/// Event raised when a user's password is changed.
/// </summary>
public class UserPasswordChangedEvent : IDomainEvent
{
    public string UserId { get; }
    public string UserName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public UserPasswordChangedEvent(string userId, string userName)
    {
        UserId = userId;
        UserName = userName;
    }
}

/// <summary>
/// Event raised when a user's account status is changed.
/// </summary>
public class UserAccountStatusChangedEvent : IDomainEvent
{
    public string UserId { get; }
    public string UserName { get; }
    public string OldStatus { get; }
    public string NewStatus { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public UserAccountStatusChangedEvent(string userId, string userName, string oldStatus, string newStatus)
    {
        UserId = userId;
        UserName = userName;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }
}