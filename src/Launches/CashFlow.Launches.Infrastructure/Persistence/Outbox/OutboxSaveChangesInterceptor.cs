using CashFlow.Launches.Application.Abstractions;
using CashFlow.Messaging;
using CashFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CashFlow.Launches.Infrastructure.Persistence.Outbox;

/// <summary>
/// Turns the domain events raised during a use case into outbox rows, inside the very
/// <c>SaveChanges</c> call that persists the business change.
/// </summary>
/// <remarks>
/// Because both inserts share one transaction, "entry saved but event lost" and
/// "event published but entry rolled back" are impossible by construction.
/// </remarks>
internal sealed class OutboxSaveChangesInterceptor(
    IIntegrationEventFactory integrationEventFactory,
    IntegrationEventRegistry registry) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AppendOutboxMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AppendOutboxMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AppendOutboxMessages(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var aggregates = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        if (aggregates.Count == 0)
        {
            return;
        }

        var messages = new List<OutboxMessage>();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var integrationEvent = integrationEventFactory.TryCreate(domainEvent);

                if (integrationEvent is null)
                {
                    continue;
                }

                messages.Add(new OutboxMessage
                {
                    Id = integrationEvent.EventId,
                    Type = registry.GetWireName(integrationEvent.GetType()),
                    Payload = IntegrationEventSerializer.Serialize(integrationEvent),
                    OccurredAtUtc = integrationEvent.OccurredAtUtc,
                });
            }

            aggregate.ClearDomainEvents();
        }

        if (messages.Count > 0)
        {
            context.Set<OutboxMessage>().AddRange(messages);
        }
    }
}
