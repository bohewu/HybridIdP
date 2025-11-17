using Core.Domain.Events;

namespace Core.Domain.Entities;

/// <summary>
/// Domain event raised when an audit event is logged.
/// </summary>
public class AuditEventLoggedEvent : IDomainEvent
{
    public int AuditEventId { get; }
    public string EventType { get; }
    public string? UserId { get; }
    public DateTime OccurredOn { get; }

    public AuditEventLoggedEvent(int auditEventId, string eventType, string? userId)
    {
        AuditEventId = auditEventId;
        EventType = eventType;
        UserId = userId;
        OccurredOn = DateTime.UtcNow;
    }
}