using CashFlow.Messaging;
using CashFlow.Messaging.Contracts;

namespace CashFlow.Consolidation.Application.Projection;

/// <summary>Applies a newly registered entry to the merchant's consolidated day.</summary>
public sealed class EntryRegisteredIntegrationEventHandler(IDailyBalanceProjection projection)
    : IIntegrationEventHandler<EntryRegisteredIntegrationEvent>
{
    public Task HandleAsync(EntryRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        projection.ApplyAsync(integrationEvent, cancellationToken);
}

/// <summary>Compensates a cancelled entry in the merchant's consolidated day.</summary>
public sealed class EntryCancelledIntegrationEventHandler(IDailyBalanceProjection projection)
    : IIntegrationEventHandler<EntryCancelledIntegrationEvent>
{
    public Task HandleAsync(EntryCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        projection.ApplyAsync(integrationEvent, cancellationToken);
}
