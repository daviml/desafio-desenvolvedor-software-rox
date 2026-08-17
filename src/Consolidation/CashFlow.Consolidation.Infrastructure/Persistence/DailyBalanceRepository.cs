using CashFlow.Consolidation.Domain.DailyBalances;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

internal sealed class DailyBalanceRepository(ConsolidationDbContext context) : IDailyBalanceRepository
{
    public Task<DailyBalance?> FindAsync(
        MerchantId merchantId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        context.DailyBalances.FirstOrDefaultAsync(
            balance => balance.MerchantId == merchantId && balance.Date == date,
            cancellationToken);

    public void Add(DailyBalance dailyBalance) => context.DailyBalances.Add(dailyBalance);
}
