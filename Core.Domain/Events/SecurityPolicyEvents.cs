using Core.Domain.Events;

namespace Core.Domain.Events;

/// <summary>
/// Event raised when security policies are updated.
/// </summary>
public class SecurityPolicyUpdatedEvent : IDomainEvent
{
    public string UpdatedByUserId { get; }
    public string UpdatedByUserName { get; }
    public string PolicyChanges { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public SecurityPolicyUpdatedEvent(string updatedByUserId, string updatedByUserName, string policyChanges)
    {
        UpdatedByUserId = updatedByUserId;
        UpdatedByUserName = updatedByUserName;
        PolicyChanges = policyChanges;
    }
}