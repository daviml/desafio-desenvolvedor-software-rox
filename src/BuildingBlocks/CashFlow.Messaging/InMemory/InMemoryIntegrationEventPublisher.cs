using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CashFlow.Messaging.InMemory;

/// <summary>
/// Transport-free publisher used by automated tests and by the "no broker" local profile.
/// It dispatches to in-process handlers inside a fresh DI scope, mirroring what the RabbitMQ
/// consumer does, so the same handler code is exercised without infrastructure.
/// </summary>
/// <remarks>
/// This is a single-process transport: it cannot deliver events to another running service.
/// Cross-service delivery requires the RabbitMQ provider.
/// </remarks>
public sealed class InMemoryIntegrationEventPublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<InMemoryIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Publishing {EventType} {EventId} through the in-memory transport",
                integrationEvent.GetType().Name,
                integrationEvent.EventId);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        await dispatcher.DispatchAsync(integrationEvent, cancellationToken);
    }
}
