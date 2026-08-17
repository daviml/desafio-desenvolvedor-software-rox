namespace CashFlow.Consolidation.Application.Abstractions;

/// <summary>
/// Deduplication ledger. Delivery is at-least-once, so the same event can arrive more than once;
/// recording the ids that were already applied is what turns that into an exactly-once *effect*
/// on the balance.
/// </summary>
public interface IProcessedEventStore
{
    Task<bool> HasProcessedAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the event as processed. The row is written in the same transaction as the projection
    /// update, so the two can never disagree.
    /// </summary>
    void MarkProcessed(Guid eventId, string eventType, DateTimeOffset processedAtUtc);
}

/// <summary>Raised when the same event is applied twice concurrently and the database rejects the duplicate.</summary>
public sealed class DuplicateProcessedEventException : Exception
{
    public DuplicateProcessedEventException() : base("This event has already been processed.")
    {
    }

    public DuplicateProcessedEventException(string message) : base(message)
    {
    }

    public DuplicateProcessedEventException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when an optimistic concurrency check fails; the caller should retry on fresh state.</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException() : base("The record was modified by another operation.")
    {
    }

    public ConcurrencyConflictException(string message) : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
