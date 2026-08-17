using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CashFlow.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidation.IntegrationTests;

/// <summary>
/// Covers the full read path: an entry event arrives, the projection is updated, and the report
/// endpoint serves the consolidated numbers.
/// </summary>
public sealed class DailyBalanceProjectionTests(ConsolidationApiFactory factory)
    : IClassFixture<ConsolidationApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly DateOnly Date = new(2026, 3, 15);

    private static EntryRegisteredIntegrationEvent Registered(
        Guid merchantId,
        EntryType type,
        decimal amount,
        DateOnly? date = null) => new()
        {
            EntryId = Guid.NewGuid(),
            MerchantId = merchantId,
            Type = type,
            Amount = amount,
            Currency = "BRL",
            EntryDate = date ?? Date,
            Description = "Movimento",
        };

    private Task<DailyBalancePayload?> GetDailyBalanceAsync(Guid merchantId, DateOnly date) =>
        factory.CreateClient().GetFromJsonAsync<DailyBalancePayload>(
            new Uri($"/api/v1/merchants/{merchantId}/daily-balance/{date:yyyy-MM-dd}", UriKind.Relative),
            Json);

    [Fact]
    public async Task RegisteredEvents_AreConsolidatedIntoTheDailyBalance()
    {
        var merchantId = Guid.NewGuid();

        await factory.DispatchAsync(Registered(merchantId, EntryType.Credit, 1_000m));
        await factory.DispatchAsync(Registered(merchantId, EntryType.Credit, 250.50m));
        await factory.DispatchAsync(Registered(merchantId, EntryType.Debit, 300m));

        var balance = await GetDailyBalanceAsync(merchantId, Date);

        balance.ShouldNotBeNull();
        balance.TotalCredits.ShouldBe(1_250.50m);
        balance.TotalDebits.ShouldBe(300m);
        balance.Balance.ShouldBe(950.50m);
        balance.CreditCount.ShouldBe(2);
        balance.DebitCount.ShouldBe(1);
        balance.EntryCount.ShouldBe(3);
    }

    [Fact]
    public async Task TheSameEventDeliveredTwice_IsAppliedOnlyOnce()
    {
        var merchantId = Guid.NewGuid();
        var integrationEvent = Registered(merchantId, EntryType.Credit, 500m);

        await factory.DispatchAsync(integrationEvent);
        await factory.DispatchAsync(integrationEvent);
        await factory.DispatchAsync(integrationEvent);

        var balance = await GetDailyBalanceAsync(merchantId, Date);

        balance!.TotalCredits.ShouldBe(500m);
        balance.CreditCount.ShouldBe(1);

        // Compare the value object itself: the converted column is what the provider understands.
        var merchant = new Domain.DailyBalances.MerchantId(merchantId);

        var consolidatedDays = await factory.QueryDatabaseAsync(context => context.DailyBalances
            .CountAsync(dailyBalance => dailyBalance.MerchantId == merchant));

        consolidatedDays.ShouldBe(1);
    }

    [Fact]
    public async Task CancellationEvents_CompensateThePreviouslyAppliedEntry()
    {
        var merchantId = Guid.NewGuid();
        var registered = Registered(merchantId, EntryType.Credit, 800m);

        await factory.DispatchAsync(registered);
        await factory.DispatchAsync(new EntryCancelledIntegrationEvent
        {
            EntryId = registered.EntryId,
            MerchantId = merchantId,
            Type = EntryType.Credit,
            Amount = 800m,
            Currency = "BRL",
            EntryDate = Date,
            Reason = "estorno",
        });

        var balance = await GetDailyBalanceAsync(merchantId, Date);

        balance!.TotalCredits.ShouldBe(0m);
        balance.Balance.ShouldBe(0m);
        balance.CreditCount.ShouldBe(0);
    }

    [Fact]
    public async Task DaysAreConsolidatedIndependently()
    {
        var merchantId = Guid.NewGuid();

        await factory.DispatchAsync(Registered(merchantId, EntryType.Credit, 100m, new DateOnly(2026, 4, 1)));
        await factory.DispatchAsync(Registered(merchantId, EntryType.Credit, 200m, new DateOnly(2026, 4, 2)));

        (await GetDailyBalanceAsync(merchantId, new DateOnly(2026, 4, 1)))!.Balance.ShouldBe(100m);
        (await GetDailyBalanceAsync(merchantId, new DateOnly(2026, 4, 2)))!.Balance.ShouldBe(200m);
    }

    [Fact]
    public async Task ADayWithoutMovement_ReturnsZeroesInsteadOfNotFound()
    {
        var balance = await GetDailyBalanceAsync(Guid.NewGuid(), new DateOnly(2026, 1, 1));

        balance!.Balance.ShouldBe(0m);
        balance.EntryCount.ShouldBe(0);
    }

    [Fact]
    public async Task Statement_ReportsOpeningMovementAndClosingBalances()
    {
        var merchantId = Guid.NewGuid();

        await factory.DispatchAsync(Registered(merchantId, EntryType.Credit, 1_000m, new DateOnly(2026, 5, 1)));
        await factory.DispatchAsync(Registered(merchantId, EntryType.Credit, 400m, new DateOnly(2026, 5, 3)));
        await factory.DispatchAsync(Registered(merchantId, EntryType.Debit, 150m, new DateOnly(2026, 5, 3)));
        await factory.DispatchAsync(Registered(merchantId, EntryType.Debit, 50m, new DateOnly(2026, 5, 4)));

        var statement = await factory.CreateClient().GetFromJsonAsync<StatementPayload>(
            new Uri($"/api/v1/merchants/{merchantId}/statement?from=2026-05-03&to=2026-05-04", UriKind.Relative),
            Json);

        statement.ShouldNotBeNull();
        statement.OpeningBalance.ShouldBe(1_000m);
        statement.TotalCredits.ShouldBe(400m);
        statement.TotalDebits.ShouldBe(200m);
        statement.NetBalance.ShouldBe(200m);
        statement.ClosingBalance.ShouldBe(1_200m);
        statement.Days.Count.ShouldBe(2);
        statement.Days[0].AccumulatedBalance.ShouldBe(1_250m);
        statement.Days[1].AccumulatedBalance.ShouldBe(1_200m);
    }

    [Fact]
    public async Task Statement_WithAnInvertedPeriod_ReturnsAValidationProblem()
    {
        var response = await factory.CreateClient().GetAsync(
            new Uri($"/api/v1/merchants/{Guid.NewGuid()}/statement?from=2026-05-10&to=2026-05-01", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Statement_LongerThanAYear_IsRejected()
    {
        var response = await factory.CreateClient().GetAsync(
            new Uri($"/api/v1/merchants/{Guid.NewGuid()}/statement?from=2024-01-01&to=2026-01-01", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Health_ReadinessReportsTheDatabase()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed record DailyBalancePayload(
        Guid MerchantId,
        DateOnly Date,
        string Currency,
        decimal TotalCredits,
        decimal TotalDebits,
        decimal Balance,
        int CreditCount,
        int DebitCount,
        int EntryCount);

    private sealed record StatementDayPayload(
        DateOnly Date,
        decimal TotalCredits,
        decimal TotalDebits,
        decimal Balance,
        decimal AccumulatedBalance,
        int EntryCount);

    private sealed record StatementPayload(
        Guid MerchantId,
        DateOnly From,
        DateOnly To,
        string Currency,
        decimal OpeningBalance,
        decimal TotalCredits,
        decimal TotalDebits,
        decimal NetBalance,
        decimal ClosingBalance,
        int EntryCount,
        List<StatementDayPayload> Days);
}
