using CashFlow.Consolidation.Application.Reports.GetDailyBalance;
using CashFlow.Consolidation.Application.Reports.GetStatement;
using CashFlow.Consolidation.Domain.DailyBalances;
using NSubstitute;

namespace CashFlow.Consolidation.UnitTests.Application;

public sealed class ReportQueryHandlerTests
{
    private static readonly Guid MerchantGuid = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly IDailyBalanceQueries _queries = Substitute.For<IDailyBalanceQueries>();

    private static DailyBalanceSnapshot Snapshot(DateOnly date, decimal credits, decimal debits) => new(
        MerchantGuid,
        date,
        "BRL",
        credits,
        debits,
        credits - debits,
        1,
        1,
        new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero));

    [Fact]
    public async Task GetDailyBalance_ReturnsTheConsolidatedDay()
    {
        var date = new DateOnly(2026, 3, 15);
        _queries.GetAsync(new MerchantId(MerchantGuid), date, Arg.Any<CancellationToken>())
            .Returns(Snapshot(date, 500m, 120m));

        var result = await new GetDailyBalanceQueryHandler(_queries)
            .HandleAsync(new GetDailyBalanceQuery(MerchantGuid, date), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCredits.ShouldBe(500m);
        result.Value.TotalDebits.ShouldBe(120m);
        result.Value.Balance.ShouldBe(380m);
        result.Value.EntryCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetDailyBalance_ForADayWithoutMovement_ReturnsZeroesRatherThanNotFound()
    {
        var date = new DateOnly(2026, 3, 16);
        _queries.GetAsync(Arg.Any<MerchantId>(), date, Arg.Any<CancellationToken>())
            .Returns((DailyBalanceSnapshot?)null);

        var result = await new GetDailyBalanceQueryHandler(_queries)
            .HandleAsync(new GetDailyBalanceQuery(MerchantGuid, date), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Balance.ShouldBe(0m);
        result.Value.EntryCount.ShouldBe(0);
        result.Value.LastUpdatedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task GetStatement_CarriesTheOpeningBalanceIntoTheRunningTotal()
    {
        var from = new DateOnly(2026, 3, 10);
        var to = new DateOnly(2026, 3, 12);

        _queries.GetAccumulatedBalanceBeforeAsync(new MerchantId(MerchantGuid), from, Arg.Any<CancellationToken>())
            .Returns(1_000m);

        _queries.GetRangeAsync(new MerchantId(MerchantGuid), from, to, Arg.Any<CancellationToken>())
            .Returns(new List<DailyBalanceSnapshot>
            {
                Snapshot(new DateOnly(2026, 3, 10), 200m, 50m),
                Snapshot(new DateOnly(2026, 3, 11), 0m, 300m),
                Snapshot(new DateOnly(2026, 3, 12), 500m, 100m),
            });

        var result = await new GetStatementQueryHandler(_queries)
            .HandleAsync(new GetStatementQuery(MerchantGuid, from, to), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var statement = result.Value;
        statement.OpeningBalance.ShouldBe(1_000m);
        statement.TotalCredits.ShouldBe(700m);
        statement.TotalDebits.ShouldBe(450m);
        statement.NetBalance.ShouldBe(250m);
        statement.ClosingBalance.ShouldBe(1_250m);
        statement.Days.Count.ShouldBe(3);
        statement.Days[0].AccumulatedBalance.ShouldBe(1_150m);
        statement.Days[1].AccumulatedBalance.ShouldBe(850m);
        statement.Days[2].AccumulatedBalance.ShouldBe(1_250m);
    }

    [Fact]
    public async Task GetStatement_WithoutMovement_ReturnsTheOpeningBalanceAsTheClosingBalance()
    {
        var from = new DateOnly(2026, 3, 10);
        var to = new DateOnly(2026, 3, 12);

        _queries.GetAccumulatedBalanceBeforeAsync(Arg.Any<MerchantId>(), from, Arg.Any<CancellationToken>())
            .Returns(42m);
        _queries.GetRangeAsync(Arg.Any<MerchantId>(), from, to, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await new GetStatementQueryHandler(_queries)
            .HandleAsync(new GetStatementQuery(MerchantGuid, from, to), CancellationToken.None);

        result.Value.ClosingBalance.ShouldBe(42m);
        result.Value.Days.ShouldBeEmpty();
        result.Value.Currency.ShouldBe("BRL");
    }

    [Theory]
    [InlineData(0, 5, true)]
    [InlineData(5, 0, false)]
    [InlineData(0, 400, false)]
    public void GetStatementValidator_BoundsThePeriod(int fromOffset, int toOffset, bool expectedValid)
    {
        var reference = new DateOnly(2026, 3, 10);
        var query = new GetStatementQuery(
            MerchantGuid,
            reference.AddDays(fromOffset),
            reference.AddDays(toOffset));

        new GetStatementQueryValidator().Validate(query).IsValid.ShouldBe(expectedValid);
    }
}
