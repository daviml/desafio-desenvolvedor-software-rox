using CashFlow.Consolidation.Application.Reports;
using CashFlow.Consolidation.Application.Reports.GetDailyBalance;
using CashFlow.Consolidation.Application.Reports.GetStatement;
using CashFlow.SharedKernel.Application;
using CashFlow.Web;

namespace CashFlow.Consolidation.Api.Endpoints;

/// <summary>
/// Read-only reporting surface. Responses are served from the pre-aggregated projection and are
/// cacheable, which is what lets this service absorb the reporting peak.
/// </summary>
internal static class DailyBalanceEndpoints
{
    /// <summary>Output cache policy name shared by the report endpoints.</summary>
    public const string ReportCachePolicy = "consolidated-reports";

    public static IEndpointRouteBuilder MapDailyBalanceEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/v1/merchants/{merchantId:guid}")
            .WithTags("Consolidated balance")
            .CacheOutput(ReportCachePolicy);

        group.MapGet("/daily-balance/{date}", GetDailyBalanceAsync)
            .WithName("GetDailyBalance")
            .WithSummary("Consolidated balance of a merchant on a given day.")
            .WithDescription(
                "Returns credits, debits and the net balance for the requested business day. " +
                "A day with no movement returns zeroed totals rather than 404.")
            .Produces<DailyBalanceResponse>()
            .ProducesValidationProblem();

        group.MapGet("/daily-balance", GetTodayAsync)
            .WithName("GetTodayBalance")
            .WithSummary("Consolidated balance of a merchant today (UTC).")
            .Produces<DailyBalanceResponse>()
            .ProducesValidationProblem();

        group.MapGet("/statement", GetStatementAsync)
            .WithName("GetStatement")
            .WithSummary("Consolidated statement for a period.")
            .WithDescription(
                "Opening balance, per-day movement with a running balance, and closing balance. " +
                "The period is limited to one year per request.")
            .Produces<StatementResponse>()
            .ProducesValidationProblem();

        return builder;
    }

    private static async Task<IResult> GetDailyBalanceAsync(
        Guid merchantId,
        DateOnly date,
        IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new GetDailyBalanceQuery(merchantId, date), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetTodayAsync(
        Guid merchantId,
        IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await dispatcher.SendAsync(new GetDailyBalanceQuery(merchantId, today), cancellationToken);

        return result.ToHttpResult();
    }

    private static async Task<IResult> GetStatementAsync(
        Guid merchantId,
        DateOnly from,
        DateOnly to,
        IRequestDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new GetStatementQuery(merchantId, from, to), cancellationToken);
        return result.ToHttpResult();
    }
}
