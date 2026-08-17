using CashFlow.Consolidation.Domain.DailyBalances;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Results;
using FluentValidation;

namespace CashFlow.Consolidation.Application.Reports.GetStatement;

/// <summary>Consolidated statement for a period, with opening, per-day and closing balances.</summary>
public sealed record GetStatementQuery(Guid MerchantId, DateOnly From, DateOnly To)
    : IQuery<StatementResponse>;

public sealed class GetStatementQueryValidator : AbstractValidator<GetStatementQuery>
{
    /// <summary>Bounds the response size and the work a single request can ask of the database.</summary>
    public const int MaxRangeDays = 366;

    public GetStatementQueryValidator()
    {
        RuleFor(query => query.MerchantId).NotEmpty();
        RuleFor(query => query.From).NotEqual(default(DateOnly)).WithMessage("From is required.");
        RuleFor(query => query.To).NotEqual(default(DateOnly)).WithMessage("To is required.");

        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From)
            .WithMessage("'To' must be on or after 'From'.");

        RuleFor(query => query)
            .Must(query => query.To.DayNumber - query.From.DayNumber < MaxRangeDays)
            .WithMessage($"The period must not exceed {MaxRangeDays} days.")
            .When(query => query.To >= query.From);
    }
}

public sealed class GetStatementQueryHandler(IDailyBalanceQueries dailyBalances)
    : IRequestHandler<GetStatementQuery, StatementResponse>
{
    public async Task<Result<StatementResponse>> HandleAsync(
        GetStatementQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var merchantId = new MerchantId(request.MerchantId);

        var openingBalance = await dailyBalances.GetAccumulatedBalanceBeforeAsync(
            merchantId,
            request.From,
            cancellationToken);

        var days = await dailyBalances.GetRangeAsync(merchantId, request.From, request.To, cancellationToken);

        var currency = days.Count > 0 ? days[0].Currency : Money.DefaultCurrency;
        var runningBalance = openingBalance;
        var statementDays = new List<StatementDayResponse>(days.Count);

        foreach (var day in days)
        {
            runningBalance += day.Balance;

            statementDays.Add(new StatementDayResponse(
                day.Date,
                day.TotalCredits,
                day.TotalDebits,
                day.Balance,
                runningBalance,
                day.CreditCount + day.DebitCount));
        }

        var totalCredits = days.Sum(day => day.TotalCredits);
        var totalDebits = days.Sum(day => day.TotalDebits);

        var statement = new StatementResponse(
            request.MerchantId,
            request.From,
            request.To,
            currency,
            openingBalance,
            totalCredits,
            totalDebits,
            totalCredits - totalDebits,
            runningBalance,
            days.Sum(day => day.CreditCount + day.DebitCount),
            statementDays);

        return statement;
    }
}
