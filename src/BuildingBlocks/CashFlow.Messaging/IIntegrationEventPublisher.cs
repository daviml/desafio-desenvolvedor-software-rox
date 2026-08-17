namespace CashFlow.Messaging;

/// <summary>
/// Sends integration events to the broker. The application layer depends on this abstraction only,
/// so RabbitMQ can be replaced (SQS, Kafka, in-memory for tests) without touching business code.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
