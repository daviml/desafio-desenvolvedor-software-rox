namespace CashFlow.Messaging.Contracts;

/// <summary>
/// Published by the Launches service once an entry has been durably stored.
/// The consolidation service uses it to update the daily balance projection.
/// </summary>
public sealed record EntryRegisteredIntegrationEvent : IntegrationEvent
{
    public const string WireName = "cashflow.entry.registered";

    public required Guid EntryId { get; init; }

    public required Guid MerchantId { get; init; }

    public required EntryType Type { get; init; }

    /// <summary>Always a positive amount; the sign is carried by <see cref="Type"/>.</summary>
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Business day the entry belongs to, which is what the daily report groups by.</summary>
    public required DateOnly EntryDate { get; init; }

    public string? Description { get; init; }
}
