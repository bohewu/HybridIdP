using Core.Domain.Events;

namespace Core.Domain.Events;

/// <summary>
/// Event raised when a client is created.
/// </summary>
public class ClientCreatedEvent : IDomainEvent
{
    public string ClientId { get; }
    public string ClientName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ClientCreatedEvent(string clientId, string clientName)
    {
        ClientId = clientId;
        ClientName = clientName;
    }
}

/// <summary>
/// Event raised when a client is updated.
/// </summary>
public class ClientUpdatedEvent : IDomainEvent
{
    public string ClientId { get; }
    public string ClientName { get; }
    public string Changes { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ClientUpdatedEvent(string clientId, string clientName, string changes)
    {
        ClientId = clientId;
        ClientName = clientName;
        Changes = changes;
    }
}

/// <summary>
/// Event raised when a client is deleted.
/// </summary>
public class ClientDeletedEvent : IDomainEvent
{
    public string ClientId { get; }
    public string ClientName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ClientDeletedEvent(string clientId, string clientName)
    {
        ClientId = clientId;
        ClientName = clientName;
    }
}

/// <summary>
/// Event raised when a client's secret is changed.
/// </summary>
public class ClientSecretChangedEvent : IDomainEvent
{
    public string ClientId { get; }
    public string ClientName { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ClientSecretChangedEvent(string clientId, string clientName)
    {
        ClientId = clientId;
        ClientName = clientName;
    }
}

/// <summary>
/// Event raised when a client's allowed scopes are changed.
/// </summary>
public class ClientScopeChangedEvent : IDomainEvent
{
    public string ClientId { get; }
    public string ClientName { get; }
    public string ScopeChanges { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public ClientScopeChangedEvent(string clientId, string clientName, string scopeChanges)
    {
        ClientId = clientId;
        ClientName = clientName;
        ScopeChanges = scopeChanges;
    }
}