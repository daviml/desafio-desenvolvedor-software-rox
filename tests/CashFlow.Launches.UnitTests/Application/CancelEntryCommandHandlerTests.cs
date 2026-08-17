using CashFlow.Launches.Application.Entries.CancelEntry;
using CashFlow.Launches.Domain.Entries;
using CashFlow.Launches.UnitTests.TestSupport;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Results;
using NSubstitute;

namespace CashFlow.Launches.UnitTests.Application;

public sealed class CancelEntryCommandHandlerTests
{
    private readonly IEntryRepository _entries = Substitute.For<IEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = FixedClock.Default;

    private CancelEntryCommandHandler CreateHandler() => new(_entries, _unitOfWork, _clock);

    private Entry CreateEntry() => Entry.Register(
        MerchantId.New(),
        EntryType.Debit,
        Money.From(80m),
        _clock.Today,
        "Compra de insumos",
        _clock);

    [Fact]
    public async Task HandleAsync_CancelsTheEntryAndCommits()
    {
        var entry = CreateEntry();
        _entries.GetByIdAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);

        var result = await CreateHandler()
            .HandleAsync(new CancelEntryCommand(entry.Id.Value, "erro de digitação"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(EntryStatus.Cancelled);
        result.Value.CancellationReason.ShouldBe("erro de digitação");

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTheEntryDoesNotExist_ReturnsNotFound()
    {
        var missingId = Guid.NewGuid();
        _entries.GetByIdAsync(Arg.Any<EntryId>(), Arg.Any<CancellationToken>()).Returns((Entry?)null);

        var result = await CreateHandler().HandleAsync(new CancelEntryCommand(missingId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("entry.not_found");
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyCancelled_ReturnsConflict()
    {
        var entry = CreateEntry();
        entry.Cancel("primeira", _clock);
        _entries.GetByIdAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);

        var result = await CreateHandler()
            .HandleAsync(new CancelEntryCommand(entry.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
