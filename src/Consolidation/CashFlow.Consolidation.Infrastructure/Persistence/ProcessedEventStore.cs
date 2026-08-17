using CashFlow.Consolidation.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

internal sealed class ProcessedEventStore(ConsolidationDbContext context) : IProcessedEventStore
{
    public Task<bool> HasProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
        context.ProcessedEvents.AnyAsync(processed => processed.EventId == eventId, cancellationToken);

    public void MarkProcessed(Guid eventId, string eventType, DateTimeOffset processedAtUtc) =>
        context.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAtUtc = processedAtUtc,
        });
}
