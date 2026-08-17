using CashFlow.SharedKernel.Domain;

namespace CashFlow.Launches.Domain.Entries.Events;

public sealed record EntryRegisteredDomainEvent(
    EntryId EntryId,
    MerchantId MerchantId,
    EntryType Type,
    Money Amount,
    DateOnly EntryDate,
    string Description,
    DateTimeOffset RegisteredAtUtc) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAtUtc { get; } = RegisteredAtUtc;
}
