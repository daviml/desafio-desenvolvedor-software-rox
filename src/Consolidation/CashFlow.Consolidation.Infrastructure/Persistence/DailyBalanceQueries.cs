using CashFlow.Consolidation.Domain.DailyBalances;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>
/// Read side of the consolidation service. Every query is a covered, no-tracking read against the
/// (merchant_id, date) unique index - the shape that keeps the reporting endpoint cheap under load.
/// </summary>
internal sealed class DailyBalanceQueries(ConsolidationDbContext context) : IDailyBalanceQueries
{
    public Task<DailyBalanceSnapshot?> GetAsync(
        MerchantId merchantId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        context.DailyBalances
            .AsNoTracking()
            .Where(balance => balance.MerchantId == merchantId && balance.Date == date)
            .Select(balance => ToSnapshot(balance))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DailyBalanceSnapshot>> GetRangeAsync(
        MerchantId merchantId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        await context.DailyBalances
            .AsNoTracking()
            .Where(balance =>
                balance.MerchantId == merchantId
                && balance.Date >= from
                && balance.Date <= to)
            .OrderBy(balance => balance.Date)
            .Select(balance => ToSnapshot(balance))
            .ToListAsync(cancellationToken);

    public async Task<decimal> GetAccumulatedBalanceBeforeAsync(
        MerchantId merchantId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        await context.DailyBalances
            .AsNoTracking()
            .Where(balance => balance.MerchantId == merchantId && balance.Date < date)
            .SumAsync(balance => balance.TotalCredits.Amount - balance.TotalDebits.Amount, cancellationToken);

    /// <summary>Shared projection expression, inlined by EF into each query's SELECT list.</summary>
    private static DailyBalanceSnapshot ToSnapshot(DailyBalance balance) => new(
        balance.MerchantId.Value,
        balance.Date,
        balance.Currency,
        balance.TotalCredits.Amount,
        balance.TotalDebits.Amount,
        balance.TotalCredits.Amount - balance.TotalDebits.Amount,
        balance.CreditCount,
        balance.DebitCount,
        balance.LastUpdatedAtUtc);
}
