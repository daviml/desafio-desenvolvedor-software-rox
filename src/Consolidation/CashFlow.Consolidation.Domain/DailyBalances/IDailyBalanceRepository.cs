namespace CashFlow.Consolidation.Domain.DailyBalances;

/// <summary>Write-side access to the projection.</summary>
public interface IDailyBalanceRepository
{
    Task<DailyBalance?> FindAsync(MerchantId merchantId, DateOnly date, CancellationToken cancellationToken);

    void Add(DailyBalance dailyBalance);
}

/// <summary>Read-side access, separated from the write model (CQRS).</summary>
public interface IDailyBalanceQueries
{
    Task<DailyBalanceSnapshot?> GetAsync(
        MerchantId merchantId,
        DateOnly date,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DailyBalanceSnapshot>> GetRangeAsync(
        MerchantId merchantId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Net position accumulated strictly before <paramref name="date"/>. Used as the opening
    /// balance of a statement, so a period report never has to scan the whole history client-side.
    /// </summary>
    Task<decimal> GetAccumulatedBalanceBeforeAsync(
        MerchantId merchantId,
        DateOnly date,
        CancellationToken cancellationToken);
}

/// <summary>Flat read model of a consolidated day.</summary>
public sealed record DailyBalanceSnapshot(
    Guid MerchantId,
    DateOnly Date,
    string Currency,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    int CreditCount,
    int DebitCount,
    DateTimeOffset LastUpdatedAtUtc);
