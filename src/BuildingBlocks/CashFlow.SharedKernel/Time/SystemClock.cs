namespace CashFlow.SharedKernel.Time;

/// <summary>Production clock. Always UTC: storage and comparisons stay timezone-agnostic.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
