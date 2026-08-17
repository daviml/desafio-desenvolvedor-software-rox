using CashFlow.Consolidation.Domain.DailyBalances;

namespace CashFlow.Consolidation.Application.Reports;

/// <summary>Consolidated position of a single business day.</summary>
public sealed record DailyBalanceResponse(
    Guid MerchantId,
    DateOnly Date,
    string Currency,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    int CreditCount,
    int DebitCount,
    int EntryCount,
    DateTimeOffset? LastUpdatedAtUtc)
{
    public static DailyBalanceResponse FromSnapshot(DailyBalanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DailyBalanceResponse(
            snapshot.MerchantId,
            snapshot.Date,
            snapshot.Currency,
            snapshot.TotalCredits,
            snapshot.TotalDebits,
            snapshot.Balance,
            snapshot.CreditCount,
            snapshot.DebitCount,
            snapshot.CreditCount + snapshot.DebitCount,
            snapshot.LastUpdatedAtUtc);
    }

    /// <summary>
    /// A day with no movement is a legitimate answer - "zero" is information, not "not found".
    /// </summary>
    public static DailyBalanceResponse EmptyFor(Guid merchantId, DateOnly date, string currency) =>
        new(merchantId, date, currency, 0m, 0m, 0m, 0, 0, 0, null);
}

/// <summary>A day inside a statement, carrying the running balance up to and including that day.</summary>
public sealed record StatementDayResponse(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    decimal AccumulatedBalance,
    int EntryCount);

/// <summary>Consolidated statement for a period: opening balance, movement per day, closing balance.</summary>
public sealed record StatementResponse(
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
    IReadOnlyList<StatementDayResponse> Days);
