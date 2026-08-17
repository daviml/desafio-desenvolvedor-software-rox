using CashFlow.Launches.Domain.Entries;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Launches.Infrastructure.Persistence;

/// <summary>Write-side repository. Returns tracked aggregates so changes are picked up on commit.</summary>
internal sealed class EntryRepository(LaunchesDbContext context) : IEntryRepository
{
    public Task<Entry?> GetByIdAsync(EntryId id, CancellationToken cancellationToken) =>
        context.Entries.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    public Task<Entry?> FindByIdempotencyKeyAsync(
        MerchantId merchantId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        context.Entries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entry => entry.MerchantId == merchantId && entry.IdempotencyKey == idempotencyKey,
                cancellationToken);

    public void Add(Entry entry) => context.Entries.Add(entry);
}
