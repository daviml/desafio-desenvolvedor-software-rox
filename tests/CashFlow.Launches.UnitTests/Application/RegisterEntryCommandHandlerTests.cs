using CashFlow.Launches.Application.Abstractions;
using CashFlow.Launches.Application.Entries;
using CashFlow.Launches.Application.Entries.RegisterEntry;
using CashFlow.Launches.Domain.Entries;
using CashFlow.Launches.UnitTests.TestSupport;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CashFlow.Launches.UnitTests.Application;

public sealed class RegisterEntryCommandHandlerTests
{
    private static readonly Guid MerchantGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IEntryRepository _entries = Substitute.For<IEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = FixedClock.Default;

    private RegisterEntryCommandHandler CreateHandler() =>
        new(_entries, _unitOfWork, _clock, NullLogger<RegisterEntryCommandHandler>.Instance);

    private RegisterEntryCommand ValidCommand(string? idempotencyKey = null) => new(
        MerchantGuid,
        EntryType.Credit,
        120.50m,
        _clock.Today,
        "Venda no cartão",
        IdempotencyKey: idempotencyKey);

    [Fact]
    public async Task HandleAsync_PersistsTheEntryAndReturnsIt()
    {
        var result = await CreateHandler().HandleAsync(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MerchantId.ShouldBe(MerchantGuid);
        result.Value.Amount.ShouldBe(120.50m);
        result.Value.Status.ShouldBe(EntryStatus.Active);

        _entries.Received(1).Add(Arg.Any<Entry>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithAKnownIdempotencyKey_ReturnsTheOriginalEntryWithoutWriting()
    {
        var existing = Entry.Register(
            new MerchantId(MerchantGuid),
            EntryType.Credit,
            Money.From(120.50m),
            _clock.Today,
            "Venda no cartão",
            _clock,
            idempotencyKey: "abc-123");

        _entries
            .FindByIdempotencyKeyAsync(new MerchantId(MerchantGuid), "abc-123", Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateHandler().HandleAsync(ValidCommand("abc-123"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(existing.Id.Value);

        _entries.DidNotReceive().Add(Arg.Any<Entry>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTwoRequestsRaceOnTheSameKey_ReturnsTheWinningEntry()
    {
        var winner = Entry.Register(
            new MerchantId(MerchantGuid),
            EntryType.Credit,
            Money.From(120.50m),
            _clock.Today,
            "Venda no cartão",
            _clock,
            idempotencyKey: "race-key");

        _entries
            .FindByIdempotencyKeyAsync(new MerchantId(MerchantGuid), "race-key", Arg.Any<CancellationToken>())
            .Returns(_ => null, _ => winner);

        _unitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw DuplicateIdempotencyKeyException.ForKey("race-key", new InvalidOperationException()));

        var result = await CreateHandler().HandleAsync(ValidCommand("race-key"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(winner.Id.Value);
    }

    [Fact]
    public async Task HandleAsync_WithAFutureDate_FailsWithAnUnprocessableError()
    {
        var command = ValidCommand() with { EntryDate = _clock.Today.AddDays(3) };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Unprocessable);
        result.Error.Code.ShouldBe("entry.date_in_future");

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithANonPositiveAmount_FailsWithoutTouchingTheDatabase()
    {
        var command = ValidCommand() with { Amount = 0m };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("entry.amount_not_positive");

        _entries.DidNotReceive().Add(Arg.Any<Entry>());
    }

    [Fact]
    public async Task HandleAsync_WithAnUnsupportedCurrency_FailsGracefully()
    {
        var command = ValidCommand() with { Currency = "REAIS" };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("money.currency_invalid");
    }
}
