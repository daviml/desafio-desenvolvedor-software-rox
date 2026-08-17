using CashFlow.SharedKernel.Domain;

namespace CashFlow.Launches.Domain.Entries.Events;

public sealed record EntryCancelledDomainEvent(
    EntryId EntryId,
    MerchantId MerchantId,
    EntryType Type,
    Money Amount,
    DateOnly EntryDate,
    string? Reason,
    DateTimeOffset CancelledAtUtc) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAtUtc { get; } = CancelledAtUtc;
}
