namespace CashFlow.SharedKernel.Time;

/// <summary>
/// Abstracts "now" so time-dependent rules (future dates, daily closing) are deterministic in tests.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
