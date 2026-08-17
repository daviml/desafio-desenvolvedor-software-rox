using CashFlow.Launches.Application.Abstractions;
using CashFlow.Launches.Domain.Entries;
using CashFlow.Launches.Domain.Entries.Events;
using CashFlow.Launches.UnitTests.TestSupport;
using CashFlow.Messaging.Contracts;
using CashFlow.SharedKernel.Domain;
using ContractEntryType = CashFlow.Messaging.Contracts.EntryType;
using DomainEntryType = CashFlow.Launches.Domain.Entries.EntryType;

namespace CashFlow.Launches.UnitTests.Application;

public sealed class LaunchesIntegrationEventFactoryTests
{
    private readonly LaunchesIntegrationEventFactory _factory = new();
    private readonly FixedClock _clock = FixedClock.Default;

    [Fact]
    public void TryCreate_MapsTheRegistrationEventOntoThePublicContract()
    {
        var entry = Entry.Register(
            MerchantId.New(),
            DomainEntryType.Credit,
            Money.From(75.25m),
            _clock.Today,
            "Venda",
            _clock);

        var domainEvent = entry.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EntryRegisteredDomainEvent>();

        var integrationEvent = _factory.TryCreate(domainEvent).ShouldBeOfType<EntryRegisteredIntegrationEvent>();

        integrationEvent.EventId.ShouldBe(domainEvent.EventId);
        integrationEvent.EntryId.ShouldBe(entry.Id.Value);
        integrationEvent.MerchantId.ShouldBe(entry.MerchantId.Value);
        integrationEvent.Type.ShouldBe(ContractEntryType.Credit);
        integrationEvent.Amount.ShouldBe(75.25m);
        integrationEvent.Currency.ShouldBe("BRL");
        integrationEvent.EntryDate.ShouldBe(_clock.Today);
    }

    [Fact]
    public void TryCreate_MapsTheCancellationEventOntoThePublicContract()
    {
        var entry = Entry.Register(
            MerchantId.New(),
            DomainEntryType.Debit,
            Money.From(40m),
            _clock.Today,
            "Compra",
            _clock);
        entry.ClearDomainEvents();
        entry.Cancel("estorno", _clock);

        var domainEvent = entry.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EntryCancelledDomainEvent>();

        var integrationEvent = _factory.TryCreate(domainEvent).ShouldBeOfType<EntryCancelledIntegrationEvent>();

        integrationEvent.Type.ShouldBe(ContractEntryType.Debit);
        integrationEvent.Amount.ShouldBe(40m);
        integrationEvent.Reason.ShouldBe("estorno");
    }

    [Fact]
    public void TryCreate_ReturnsNullForPurelyInternalDomainEvents()
    {
        _factory.TryCreate(new InternalOnlyDomainEvent()).ShouldBeNull();
    }

    private sealed record InternalOnlyDomainEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();

        public DateTimeOffset OccurredAtUtc { get; } = DateTimeOffset.UnixEpoch;
    }
}
