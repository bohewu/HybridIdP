using Core.Domain.Events;

namespace Infrastructure.Services;

/// <summary>
/// Simple domain event publisher implementation.
/// In a real application, this would integrate with a message bus or event store.
/// </summary>
public class DomainEventPublisher : IDomainEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        // For now, just log to console. In production, this would publish to a message bus.
        Console.WriteLine($"Domain event published: {typeof(TEvent).Name} at {domainEvent.OccurredOn}");
        return Task.CompletedTask;
    }
}