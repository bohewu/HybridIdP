using Core.Domain.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services;

/// <summary>
/// Simple domain event publisher implementation.
/// In a real application, this would integrate with a message bus or event store.
/// </summary>
public class DomainEventPublisher : IDomainEventPublisher
{
    private readonly IServiceProvider _serviceProvider;

    public DomainEventPublisher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        // Log the event
        Console.WriteLine($"Domain event published: {typeof(TEvent).Name} at {domainEvent.OccurredOn}");

        // Find and invoke all handlers for this event type
        var handlerType = typeof(IDomainEventHandler<TEvent>);
        var handlers = _serviceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is IDomainEventHandler<TEvent> typedHandler)
            {
                await typedHandler.HandleAsync(domainEvent);
            }
        }
    }
}