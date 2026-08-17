using CashFlow.Messaging;
using CashFlow.Messaging.Contracts;

namespace CashFlow.Consolidation.Application.Projection;

/// <summary>Applies a newly registered entry to the merchant's consolidated day.</summary>
public sealed class EntryRegisteredIntegrationEventHandler(DailyBalanceProjector projector)
    : IIntegrationEventHandler<EntryRegisteredIntegrationEvent>
{
    public Task HandleAsync(EntryRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        projector.ApplyAsync(integrationEvent, cancellationToken);
}

/// <summary>Compensates a cancelled entry in the merchant's consolidated day.</summary>
public sealed class EntryCancelledIntegrationEventHandler(DailyBalanceProjector projector)
    : IIntegrationEventHandler<EntryCancelledIntegrationEvent>
{
    public Task HandleAsync(EntryCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        projector.ApplyAsync(integrationEvent, cancellationToken);
}
