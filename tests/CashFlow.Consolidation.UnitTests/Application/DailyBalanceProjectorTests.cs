using CashFlow.Consolidation.Application.Abstractions;
using CashFlow.Consolidation.Application.Projection;
using CashFlow.Consolidation.Domain.DailyBalances;
using CashFlow.Consolidation.UnitTests.TestSupport;
using CashFlow.Messaging.Contracts;
using CashFlow.SharedKernel.Application;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CashFlow.Consolidation.UnitTests.Application;

public sealed class DailyBalanceProjectorTests
{
    private static readonly Guid MerchantGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly Date = new(2026, 3, 15);

    private readonly IDailyBalanceRepository _repository = Substitute.For<IDailyBalanceRepository>();
    private readonly IProcessedEventStore _processedEvents = Substitute.For<IProcessedEventStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = FixedClock.Default;

    private DailyBalanceProjector CreateProjector() => new(
        _repository,
        _processedEvents,
        _unitOfWork,
        _clock,
        NullLogger<DailyBalanceProjector>.Instance);

    private static EntryRegisteredIntegrationEvent RegisteredEvent(
        EntryType type = EntryType.Credit,
        decimal amount = 100m) => new()
        {
            EntryId = Guid.NewGuid(),
            MerchantId = MerchantGuid,
            Type = type,
            Amount = amount,
            Currency = "BRL",
            EntryDate = Date,
            Description = "Venda",
        };

    private static EntryCancelledIntegrationEvent CancelledEvent(
        EntryType type = EntryType.Credit,
        decimal amount = 100m) => new()
        {
            EntryId = Guid.NewGuid(),
            MerchantId = MerchantGuid,
            Type = type,
            Amount = amount,
            Currency = "BRL",
            EntryDate = Date,
        };

    [Fact]
    public async Task ApplyAsync_OpensTheDayWhenItDoesNotExistYet()
    {
        DailyBalance? added = null;
        _repository.When(repository => repository.Add(Arg.Any<DailyBalance>()))
            .Do(call => added = call.Arg<DailyBalance>());

        await CreateProjector().ApplyAsync(RegisteredEvent(amount: 250m), CancellationToken.None);

        added.ShouldNotBeNull();
        added.MerchantId.Value.ShouldBe(MerchantGuid);
        added.Date.ShouldBe(Date);
        added.TotalCredits.Amount.ShouldBe(250m);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyAsync_AccumulatesOntoAnExistingDay()
    {
        var existing = DailyBalance.Open(new MerchantId(MerchantGuid), Date, "BRL");
        existing.ApplyCredit(CashFlow.SharedKernel.Domain.Money.From(100m), _clock.UtcNow);

        _repository.FindAsync(new MerchantId(MerchantGuid), Date, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateProjector().ApplyAsync(RegisteredEvent(EntryType.Debit, 40m), CancellationToken.None);

        existing.TotalDebits.Amount.ShouldBe(40m);
        existing.Balance.Amount.ShouldBe(60m);
        _repository.DidNotReceive().Add(Arg.Any<DailyBalance>());
    }

    [Fact]
    public async Task ApplyAsync_SkipsAnEventThatWasAlreadyProcessed()
    {
        _processedEvents.HasProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        await CreateProjector().ApplyAsync(RegisteredEvent(), CancellationToken.None);

        await _repository.DidNotReceive().FindAsync(
            Arg.Any<MerchantId>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyAsync_RecordsTheEventIdSoRedeliveryIsHarmless()
    {
        var integrationEvent = RegisteredEvent();

        await CreateProjector().ApplyAsync(integrationEvent, CancellationToken.None);

        _processedEvents.Received(1).MarkProcessed(
            integrationEvent.EventId,
            EntryRegisteredIntegrationEvent.WireName,
            FixedClock.DefaultNow);
    }

    [Fact]
    public async Task ApplyAsync_WhenAConcurrentConsumerWonTheRace_CompletesQuietly()
    {
        _unitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new DuplicateProcessedEventException());

        await Should.NotThrowAsync(() => CreateProjector().ApplyAsync(RegisteredEvent(), CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_Cancellation_ReversesTheOriginalMovement()
    {
        var existing = DailyBalance.Open(new MerchantId(MerchantGuid), Date, "BRL");
        existing.ApplyCredit(CashFlow.SharedKernel.Domain.Money.From(100m), _clock.UtcNow);

        _repository.FindAsync(new MerchantId(MerchantGuid), Date, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateProjector().ApplyAsync(CancelledEvent(EntryType.Credit, 100m), CancellationToken.None);

        existing.TotalCredits.IsZero.ShouldBeTrue();
        existing.CreditCount.ShouldBe(0);
    }

    [Fact]
    public async Task ApplyAsync_Cancellation_BeforeTheRegistrationArrived_ThrowsSoTheMessageIsRedelivered()
    {
        _repository.FindAsync(Arg.Any<MerchantId>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((DailyBalance?)null);

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateProjector().ApplyAsync(CancelledEvent(), CancellationToken.None));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
