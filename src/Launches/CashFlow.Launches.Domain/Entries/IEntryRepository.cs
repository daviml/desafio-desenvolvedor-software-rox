namespace CashFlow.Launches.Domain.Entries;

/// <summary>
/// Persistence contract owned by the domain and implemented by infrastructure
/// (Dependency Inversion: the domain does not know EF Core exists).
/// </summary>
public interface IEntryRepository
{
    Task<Entry?> GetByIdAsync(EntryId id, CancellationToken cancellationToken);

    /// <summary>Supports the idempotent registration path: a retried request returns the original entry.</summary>
    Task<Entry?> FindByIdempotencyKeyAsync(
        MerchantId merchantId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    void Add(Entry entry);
}

/// <summary>Read-only projection contract for listing entries, kept apart from the write model (CQRS).</summary>
public interface IEntryQueries
{
    Task<(IReadOnlyList<EntrySummary> Items, long TotalCount)> SearchAsync(
        EntrySearchCriteria criteria,
        CancellationToken cancellationToken);
}

/// <summary>Filters accepted by the entry listing endpoint.</summary>
public sealed record EntrySearchCriteria(
    MerchantId MerchantId,
    DateOnly? From,
    DateOnly? To,
    EntryType? Type,
    bool IncludeCancelled,
    int Page,
    int PageSize);

/// <summary>Flat read model returned by listings - never the aggregate itself.</summary>
public sealed record EntrySummary(
    Guid Id,
    Guid MerchantId,
    EntryType Type,
    decimal Amount,
    string Currency,
    DateOnly EntryDate,
    string Description,
    string? Category,
    EntryStatus Status,
    DateTimeOffset RegisteredAtUtc);
