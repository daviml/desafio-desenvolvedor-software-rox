using CashFlow.Launches.Application.Entries;
using CashFlow.Launches.Application.Entries.CancelEntry;
using CashFlow.Launches.Application.Entries.GetEntryById;
using CashFlow.Launches.Application.Entries.ListEntries;
using CashFlow.Launches.Application.Entries.RegisterEntry;
using CashFlow.Launches.Domain.Entries;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.Web;
using Microsoft.AspNetCore.Mvc;

namespace CashFlow.Launches.Api.Endpoints;

/// <summary>
/// HTTP surface of the Launches service. Endpoints are thin on purpose: parse, dispatch, translate.
/// Every decision lives in the application and domain layers, where it can be unit tested.
/// </summary>
internal static class EntryEndpoints
{
    public static IEndpointRouteBuilder MapEntryEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/v1")
            .WithTags("Entries");

        group.MapPost("/entries", RegisterAsync)
            .WithName("RegisterEntry")
            .WithSummary("Registers a credit or debit entry.")
            .WithDescription(
                "Accepts the entry, stores it and queues the consolidation event in the same transaction. " +
                "Send an 'Idempotency-Key' header to make client retries safe.")
            .Produces<EntryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/entries/{entryId:guid}/cancellation", CancelAsync)
            .WithName("CancelEntry")
            .WithSummary("Cancels an entry and compensates the consolidated balance.")
            .Produces<EntryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/entries/{entryId:guid}", GetByIdAsync)
            .WithName("GetEntryById")
            .WithSummary("Returns a single entry.")
            .Produces<EntryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/merchants/{merchantId:guid}/entries", ListAsync)
            .WithName("ListEntries")
            .WithSummary("Lists a merchant's entries, newest first.")
            .Produces<PagedResult<EntryResponse>>()
            .ProducesValidationProblem();

        return builder;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterEntryRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RegisterEntryCommand(
            request.MerchantId,
            request.Type,
            request.Amount,
            request.EntryDate,
            request.Description,
            request.Currency ?? Money.DefaultCurrency,
            request.Category,
            idempotencyKey);

        var result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToHttpResult(entry =>
            Results.CreatedAtRoute("GetEntryById", new { entryId = entry.Id }, entry));
    }

    private static async Task<IResult> CancelAsync(
        Guid entryId,
        CancelEntryRequest? request,
        IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new CancelEntryCommand(entryId, request?.Reason),
            cancellationToken);

        return result.ToHttpResult();
    }

    private static async Task<IResult> GetByIdAsync(
        Guid entryId,
        IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new GetEntryByIdQuery(entryId), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ListAsync(
        Guid merchantId,
        IRequestDispatcher dispatcher,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        EntryType? type = null,
        bool includeCancelled = true,
        int page = 1,
        int pageSize = 50)
    {
        var query = new ListEntriesQuery(merchantId, from, to, type, includeCancelled, page, pageSize);
        var result = await dispatcher.SendAsync(query, cancellationToken);

        return result.ToHttpResult();
    }
}

/// <summary>Body of POST /api/v1/entries.</summary>
/// <param name="MerchantId">Merchant the entry belongs to.</param>
/// <param name="Type">Credit or Debit.</param>
/// <param name="Amount">Positive amount; the type carries the sign.</param>
/// <param name="EntryDate">Business day of the entry (yyyy-MM-dd).</param>
/// <param name="Description">What the movement refers to.</param>
/// <param name="Currency">ISO-4217 code. Defaults to BRL.</param>
/// <param name="Category">Optional free-form classification.</param>
internal sealed record RegisterEntryRequest(
    Guid MerchantId,
    EntryType Type,
    decimal Amount,
    DateOnly EntryDate,
    string Description,
    string? Currency = null,
    string? Category = null);

/// <summary>Body of POST /api/v1/entries/{entryId}/cancellation.</summary>
internal sealed record CancelEntryRequest(string? Reason);
