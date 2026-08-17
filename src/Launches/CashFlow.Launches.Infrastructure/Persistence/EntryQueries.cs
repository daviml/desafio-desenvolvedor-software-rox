using CashFlow.Launches.Domain.Entries;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Launches.Infrastructure.Persistence;

/// <summary>
/// Read-side of the Launches service. Queries project straight into the read model and never
/// track entities: no change-tracking overhead and no accidental writes from a read path.
/// </summary>
internal sealed class EntryQueries(LaunchesDbContext context) : IEntryQueries
{
    public async Task<(IReadOnlyList<EntrySummary> Items, long TotalCount)> SearchAsync(
        EntrySearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var query = context.Entries
            .AsNoTracking()
            .Where(entry => entry.MerchantId == criteria.MerchantId);

        if (criteria.From is { } from)
        {
            query = query.Where(entry => entry.EntryDate >= from);
        }

        if (criteria.To is { } to)
        {
            query = query.Where(entry => entry.EntryDate <= to);
        }

        if (criteria.Type is { } type)
        {
            query = query.Where(entry => entry.Type == type);
        }

        if (!criteria.IncludeCancelled)
        {
            query = query.Where(entry => entry.Status == EntryStatus.Active);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        if (totalCount == 0)
        {
            return ([], 0);
        }

        var items = await query
            .OrderByDescending(entry => entry.EntryDate)
            .ThenByDescending(entry => entry.RegisteredAtUtc)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(entry => new EntrySummary(
                entry.Id.Value,
                entry.MerchantId.Value,
                entry.Type,
                entry.Amount.Amount,
                entry.Amount.Currency,
                entry.EntryDate,
                entry.Description,
                entry.Category,
                entry.Status,
                entry.RegisteredAtUtc))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
