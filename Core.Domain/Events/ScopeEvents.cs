using Core.Domain.Events;

namespace Core.Domain.Events;

/// <summary>
/// Event raised when a scope is created.
/// </summary>
public class ScopeCreatedEvent : IDomainEvent
{
    public string ScopeId { get; }
    public string ScopeName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ScopeCreatedEvent(string scopeId, string scopeName)
    {
        ScopeId = scopeId;
        ScopeName = scopeName;
    }
}

/// <summary>
/// Event raised when a scope is updated.
/// </summary>
public class ScopeUpdatedEvent : IDomainEvent
{
    public string ScopeId { get; }
    public string ScopeName { get; }
    public string Changes { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ScopeUpdatedEvent(string scopeId, string scopeName, string changes)
    {
        ScopeId = scopeId;
        ScopeName = scopeName;
        Changes = changes;
    }
}

/// <summary>
/// Event raised when a scope is deleted.
/// </summary>
public class ScopeDeletedEvent : IDomainEvent
{
    public string ScopeId { get; }
    public string ScopeName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ScopeDeletedEvent(string scopeId, string scopeName)
    {
        ScopeId = scopeId;
        ScopeName = scopeName;
    }
}

/// <summary>
/// Event raised when a scope's claims are changed.
/// </summary>
public class ScopeClaimChangedEvent : IDomainEvent
{
    public string ScopeId { get; }
    public string ScopeName { get; }
    public string ClaimChanges { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ScopeClaimChangedEvent(string scopeId, string scopeName, string claimChanges)
    {
        ScopeId = scopeId;
        ScopeName = scopeName;
        ClaimChanges = claimChanges;
    }
}