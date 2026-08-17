namespace CashFlow.Messaging;

/// <summary>Consumer-side counterpart of <see cref="IIntegrationEventPublisher"/>.</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>Routes a deserialized event to its registered handler.</summary>
public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
