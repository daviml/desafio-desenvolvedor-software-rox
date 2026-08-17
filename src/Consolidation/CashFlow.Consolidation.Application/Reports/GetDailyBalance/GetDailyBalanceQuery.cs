using CashFlow.Consolidation.Domain.DailyBalances;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Results;
using FluentValidation;

namespace CashFlow.Consolidation.Application.Reports.GetDailyBalance;

/// <summary>The core report: what a merchant's cash position was on a given day.</summary>
public sealed record GetDailyBalanceQuery(Guid MerchantId, DateOnly Date) : IQuery<DailyBalanceResponse>;

public sealed class GetDailyBalanceQueryValidator : AbstractValidator<GetDailyBalanceQuery>
{
    public GetDailyBalanceQueryValidator()
    {
        RuleFor(query => query.MerchantId).NotEmpty();
        RuleFor(query => query.Date).NotEqual(default(DateOnly)).WithMessage("Date is required.");
    }
}

/// <summary>
/// Serves a single pre-aggregated row. The read path does no arithmetic over the entry history,
/// which is what lets it absorb the reporting peak.
/// </summary>
public sealed class GetDailyBalanceQueryHandler(IDailyBalanceQueries dailyBalances)
    : IRequestHandler<GetDailyBalanceQuery, DailyBalanceResponse>
{
    public async Task<Result<DailyBalanceResponse>> HandleAsync(
        GetDailyBalanceQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = await dailyBalances.GetAsync(
            new MerchantId(request.MerchantId),
            request.Date,
            cancellationToken);

        return snapshot is null
            ? DailyBalanceResponse.EmptyFor(request.MerchantId, request.Date, Money.DefaultCurrency)
            : DailyBalanceResponse.FromSnapshot(snapshot);
    }
}
