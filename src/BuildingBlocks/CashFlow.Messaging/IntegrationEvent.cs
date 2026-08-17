namespace CashFlow.Messaging;

/// <summary>
/// A fact published to other bounded contexts. Integration events are part of a public contract:
/// they are versioned, additive-only and never expose internal domain types.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Stable identity used by consumers for idempotent (exactly-once-effect) processing.</summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Flows end-to-end so a single business operation can be traced across services.</summary>
    public string? CorrelationId { get; init; }
}
