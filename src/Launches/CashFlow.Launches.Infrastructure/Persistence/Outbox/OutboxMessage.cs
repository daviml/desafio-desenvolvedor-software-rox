namespace CashFlow.Launches.Infrastructure.Persistence.Outbox;

/// <summary>
/// An integration event waiting to be published, stored in the same database - and the same
/// transaction - as the business change that produced it.
/// </summary>
/// <remarks>
/// This is the core of the "keep accepting entries while the consolidation service is down"
/// requirement: the write path commits to PostgreSQL only, and a background dispatcher moves the
/// row to RabbitMQ afterwards. A broker outage delays consolidation; it never rejects a sale.
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Same value as the integration event id, which is what makes consumers idempotent.</summary>
    public Guid Id { get; init; }

    /// <summary>Wire name of the contract, e.g. "cashflow.entry.registered".</summary>
    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Null while pending. Set once the broker has confirmed the publish.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>Earliest moment the dispatcher may try again (exponential backoff).</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public string? LastError { get; set; }

    public void MarkPublished(DateTimeOffset now)
    {
        ProcessedAtUtc = now;
        NextAttemptAtUtc = null;
        LastError = null;
    }

    public void MarkFailed(DateTimeOffset now, TimeSpan backoff, string error)
    {
        AttemptCount++;
        NextAttemptAtUtc = now.Add(backoff);
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
