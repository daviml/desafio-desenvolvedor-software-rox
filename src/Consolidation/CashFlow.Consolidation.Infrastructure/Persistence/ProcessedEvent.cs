namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>
/// One row per integration event already folded into the projection. The primary key is the
/// event id, so the database itself refuses to apply the same event twice.
/// </summary>
internal sealed class ProcessedEvent
{
    public Guid EventId { get; init; }

    public required string EventType { get; init; }

    public DateTimeOffset ProcessedAtUtc { get; init; }
}
