namespace CashFlow.Messaging.Contracts;

/// <summary>
/// Published when an entry is cancelled. Financial records are never deleted; the consolidation
/// service compensates the daily balance by reversing the original amount.
/// </summary>
public sealed record EntryCancelledIntegrationEvent : IntegrationEvent
{
    public const string WireName = "cashflow.entry.cancelled";

    public required Guid EntryId { get; init; }

    public required Guid MerchantId { get; init; }

    public required EntryType Type { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required DateOnly EntryDate { get; init; }

    public string? Reason { get; init; }
}
