using System.ComponentModel.DataAnnotations;

namespace CashFlow.Launches.Infrastructure.Persistence.Outbox;

/// <summary>Tuning knobs of the outbox dispatcher, bound from the "Outbox" configuration section.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often the dispatcher looks for pending messages when the last sweep was empty.</summary>
    [Range(50, 60_000)]
    public int PollIntervalMilliseconds { get; set; } = 500;

    /// <summary>Messages moved per sweep. 200 messages every 500 ms comfortably exceeds the 50 msg/s peak.</summary>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 200;

    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 12;

    [Range(100, 600_000)]
    public int BaseBackoffMilliseconds { get; set; } = 1000;

    [Range(1_000, 3_600_000)]
    public int MaxBackoffMilliseconds { get; set; } = 60_000;

    /// <summary>Consecutive publish failures that open the circuit breaker and pause the dispatcher.</summary>
    [Range(1, 100)]
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    [Range(1_000, 600_000)]
    public int CircuitBreakerCooldownMilliseconds { get; set; } = 15_000;

    /// <summary>Published rows older than this are deleted so the table stays small and fast.</summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 7;

    public bool Enabled { get; set; } = true;
}
