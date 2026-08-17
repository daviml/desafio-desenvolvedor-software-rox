using CashFlow.Launches.Domain.Entries;

namespace CashFlow.Launches.Application.Entries;

/// <summary>Transport-facing representation of an entry. Decouples the API contract from the aggregate.</summary>
public sealed record EntryResponse(
    Guid Id,
    Guid MerchantId,
    EntryType Type,
    decimal Amount,
    string Currency,
    DateOnly EntryDate,
    string Description,
    string? Category,
    EntryStatus Status,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason)
{
    public static EntryResponse FromEntry(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new EntryResponse(
            entry.Id.Value,
            entry.MerchantId.Value,
            entry.Type,
            entry.Amount.Amount,
            entry.Amount.Currency,
            entry.EntryDate,
            entry.Description,
            entry.Category,
            entry.Status,
            entry.RegisteredAtUtc,
            entry.CancelledAtUtc,
            entry.CancellationReason);
    }

    public static EntryResponse FromSummary(EntrySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new EntryResponse(
            summary.Id,
            summary.MerchantId,
            summary.Type,
            summary.Amount,
            summary.Currency,
            summary.EntryDate,
            summary.Description,
            summary.Category,
            summary.Status,
            summary.RegisteredAtUtc,
            null,
            null);
    }
}
