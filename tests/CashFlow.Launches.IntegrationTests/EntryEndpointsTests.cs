using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CashFlow.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Launches.IntegrationTests;

/// <summary>End-to-end coverage of the write API, from HTTP down to the database and the outbox.</summary>
public sealed class EntryEndpointsTests(LaunchesApiFactory factory) : IClassFixture<LaunchesApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static object NewEntryPayload(
        Guid merchantId,
        string type = "Credit",
        decimal amount = 199.90m,
        DateOnly? entryDate = null,
        string description = "Venda no balcão") => new
        {
            merchantId,
            type,
            amount,
            entryDate = (entryDate ?? Today).ToString("yyyy-MM-dd"),
            description,
        };

    [Fact]
    public async Task Post_Entry_ReturnsCreatedWithTheLocationHeader()
    {
        var client = factory.CreateClient();
        var merchantId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/v1/entries", NewEntryPayload(merchantId), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var entry = await response.Content.ReadFromJsonAsync<EntryPayload>(Json);
        entry.ShouldNotBeNull();
        entry.MerchantId.ShouldBe(merchantId);
        entry.Amount.ShouldBe(199.90m);
        entry.Currency.ShouldBe("BRL");
        entry.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Post_Entry_WritesTheIntegrationEventToTheOutboxInTheSameTransaction()
    {
        var client = factory.CreateClient();
        var merchantId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/v1/entries", NewEntryPayload(merchantId), Json);
        var entry = await response.Content.ReadFromJsonAsync<EntryPayload>(Json);

        var outboxTypes = await factory.QueryDatabaseAsync(context => context.OutboxMessages
            .Where(message => message.Payload.Contains(entry!.Id.ToString()))
            .Select(message => message.Type)
            .ToListAsync());

        outboxTypes.ShouldHaveSingleItem().ShouldBe(EntryRegisteredIntegrationEvent.WireName);
    }

    [Fact]
    public async Task Post_Entry_WithTheSameIdempotencyKey_ReturnsTheOriginalEntry()
    {
        var client = factory.CreateClient();
        var merchantId = Guid.NewGuid();
        var payload = NewEntryPayload(merchantId);

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = JsonContent.Create(payload, options: Json),
        };
        first.Headers.Add("Idempotency-Key", "checkout-42");

        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = JsonContent.Create(payload, options: Json),
        };
        second.Headers.Add("Idempotency-Key", "checkout-42");

        var firstEntry = await (await client.SendAsync(first)).Content.ReadFromJsonAsync<EntryPayload>(Json);
        var secondEntry = await (await client.SendAsync(second)).Content.ReadFromJsonAsync<EntryPayload>(Json);

        secondEntry!.Id.ShouldBe(firstEntry!.Id);

        var storedCount = await factory.QueryDatabaseAsync(context => context.Entries
            .CountAsync(entry => entry.IdempotencyKey == "checkout-42"));

        storedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Post_Entry_WithAnInvalidBody_ReturnsAValidationProblem()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/entries",
            NewEntryPayload(Guid.NewGuid(), amount: -5m, description: ""),
            Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>(Json);
        problem!.Errors.ShouldContainKey("Amount");
        problem.Errors.ShouldContainKey("Description");
    }

    [Fact]
    public async Task Post_Entry_WithAFutureDate_IsRejectedByTheDomainAsUnprocessable()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/entries",
            NewEntryPayload(Guid.NewGuid(), entryDate: Today.AddDays(5)),
            Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(Json);
        problem!.Code.ShouldBe("entry.date_in_future");
    }

    [Fact]
    public async Task Get_Entry_ReturnsNotFoundForAnUnknownId()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/v1/entries/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_Cancellation_CancelsOnceAndThenConflicts()
    {
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/v1/entries", NewEntryPayload(Guid.NewGuid()), Json);
        var entry = await created.Content.ReadFromJsonAsync<EntryPayload>(Json);

        var cancelUri = new Uri($"/api/v1/entries/{entry!.Id}/cancellation", UriKind.Relative);

        var first = await client.PostAsJsonAsync(cancelUri, new { reason = "duplicado" }, Json);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cancelled = await first.Content.ReadFromJsonAsync<EntryPayload>(Json);
        cancelled!.Status.ShouldBe("Cancelled");

        var second = await client.PostAsJsonAsync(cancelUri, new { reason = "duplicado" }, Json);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Post_Cancellation_QueuesTheCompensatingEvent()
    {
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/v1/entries", NewEntryPayload(Guid.NewGuid()), Json);
        var entry = await created.Content.ReadFromJsonAsync<EntryPayload>(Json);

        await client.PostAsJsonAsync(
            new Uri($"/api/v1/entries/{entry!.Id}/cancellation", UriKind.Relative),
            new { reason = "estorno" },
            Json);

        var types = await factory.QueryDatabaseAsync(context => context.OutboxMessages
            .Where(message => message.Payload.Contains(entry.Id.ToString()))
            .Select(message => message.Type)
            .ToListAsync());

        types.ShouldContain(EntryRegisteredIntegrationEvent.WireName);
        types.ShouldContain(EntryCancelledIntegrationEvent.WireName);
    }

    [Fact]
    public async Task Get_MerchantEntries_PagesAndFiltersByType()
    {
        var client = factory.CreateClient();
        var merchantId = Guid.NewGuid();

        for (var i = 1; i <= 3; i++)
        {
            await client.PostAsJsonAsync("/api/v1/entries", NewEntryPayload(merchantId, amount: i * 10m), Json);
        }

        await client.PostAsJsonAsync(
            "/api/v1/entries",
            NewEntryPayload(merchantId, type: "Debit", amount: 5m),
            Json);

        var all = await client.GetFromJsonAsync<PagePayload>(
            new Uri($"/api/v1/merchants/{merchantId}/entries", UriKind.Relative),
            Json);

        all!.TotalCount.ShouldBe(4);
        all.Items.Count.ShouldBe(4);

        var debitsOnly = await client.GetFromJsonAsync<PagePayload>(
            new Uri($"/api/v1/merchants/{merchantId}/entries?type=Debit", UriKind.Relative),
            Json);

        debitsOnly!.TotalCount.ShouldBe(1);

        var firstPage = await client.GetFromJsonAsync<PagePayload>(
            new Uri($"/api/v1/merchants/{merchantId}/entries?page=1&pageSize=2", UriKind.Relative),
            Json);

        firstPage!.Items.Count.ShouldBe(2);
        firstPage.TotalPages.ShouldBe(2);
        firstPage.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task Get_MerchantEntries_WithAnOversizedPage_ReturnsAValidationProblem()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri($"/api/v1/merchants/{Guid.NewGuid()}/entries?pageSize=5000", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EveryResponse_CarriesACorrelationId()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/entries/{Guid.NewGuid()}");
        request.Headers.Add("X-Correlation-Id", "trace-me-123");

        var response = await client.SendAsync(request);

        response.Headers.TryGetValues("X-Correlation-Id", out var values).ShouldBeTrue();
        values!.ShouldContain("trace-me-123");
    }

    [Fact]
    public async Task Health_LivenessIsAlwaysUp()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed record EntryPayload(
        Guid Id,
        Guid MerchantId,
        string Type,
        decimal Amount,
        string Currency,
        string Status);

    private sealed record ProblemPayload(string Title, int Status, string Code);

    private sealed record ValidationProblemPayload(Dictionary<string, string[]> Errors);

    private sealed record PagePayload(
        List<EntryPayload> Items,
        int Page,
        int PageSize,
        long TotalCount,
        int TotalPages,
        bool HasNextPage);
}
