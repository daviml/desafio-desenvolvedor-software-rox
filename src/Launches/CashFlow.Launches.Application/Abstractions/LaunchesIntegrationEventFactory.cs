using CashFlow.Launches.Domain.Entries.Events;
using CashFlow.Messaging;
using CashFlow.Messaging.Contracts;
using CashFlow.SharedKernel.Domain;
using DomainEntryType = CashFlow.Launches.Domain.Entries.EntryType;

namespace CashFlow.Launches.Application.Abstractions;

/// <inheritdoc />
public sealed class LaunchesIntegrationEventFactory : IIntegrationEventFactory
{
    public IntegrationEvent? TryCreate(IDomainEvent domainEvent) => domainEvent switch
    {
        EntryRegisteredDomainEvent registered => new EntryRegisteredIntegrationEvent
        {
            EventId = registered.EventId,
            OccurredAtUtc = registered.OccurredAtUtc,
            EntryId = registered.EntryId.Value,
            MerchantId = registered.MerchantId.Value,
            Type = ToContract(registered.Type),
            Amount = registered.Amount.Amount,
            Currency = registered.Amount.Currency,
            EntryDate = registered.EntryDate,
            Description = registered.Description,
        },
        EntryCancelledDomainEvent cancelled => new EntryCancelledIntegrationEvent
        {
            EventId = cancelled.EventId,
            OccurredAtUtc = cancelled.OccurredAtUtc,
            EntryId = cancelled.EntryId.Value,
            MerchantId = cancelled.MerchantId.Value,
            Type = ToContract(cancelled.Type),
            Amount = cancelled.Amount.Amount,
            Currency = cancelled.Amount.Currency,
            EntryDate = cancelled.EntryDate,
            Reason = cancelled.Reason,
        },
        _ => null,
    };

    private static EntryType ToContract(DomainEntryType type) => type switch
    {
        DomainEntryType.Credit => EntryType.Credit,
        DomainEntryType.Debit => EntryType.Debit,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped entry type."),
    };
}
