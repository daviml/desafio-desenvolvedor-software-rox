using CashFlow.SharedKernel.Time;

namespace CashFlow.Consolidation.UnitTests.TestSupport;

/// <summary>Deterministic clock so projection timestamps can be asserted.</summary>
internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    public static FixedClock Default => new(DefaultNow);

    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
