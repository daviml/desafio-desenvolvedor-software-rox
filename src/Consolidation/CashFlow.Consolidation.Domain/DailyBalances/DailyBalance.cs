using CashFlow.SharedKernel.Domain;

namespace CashFlow.Consolidation.Domain.DailyBalances;

/// <summary>
/// One merchant's consolidated position for one business day: a projection maintained incrementally
/// from the entry events, so the report is a single indexed row read instead of an aggregation
/// over the whole history.
/// </summary>
public sealed class DailyBalance : AggregateRoot<Guid>
{
    private DailyBalance(Guid id, MerchantId merchantId, DateOnly date, string currency) : base(id)
    {
        MerchantId = merchantId;
        Date = date;
        Currency = currency;
        TotalCredits = Money.ZeroIn(currency);
        TotalDebits = Money.ZeroIn(currency);
    }

    /// <summary>Required by EF Core.</summary>
    private DailyBalance()
    {
        Currency = Money.DefaultCurrency;
    }

    public MerchantId MerchantId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Currency { get; private set; }

    public Money TotalCredits { get; private set; }

    public Money TotalDebits { get; private set; }

    public int CreditCount { get; private set; }

    public int DebitCount { get; private set; }

    public DateTimeOffset LastUpdatedAtUtc { get; private set; }

    /// <summary>
    /// Optimistic concurrency token. Several consumer threads can touch the same day at once;
    /// whoever loses the race retries on a fresh read instead of silently overwriting a balance.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Net position of the day: credits minus debits. Derived, never stored twice.</summary>
    public Money Balance => TotalCredits.Subtract(TotalDebits);

    public int EntryCount => CreditCount + DebitCount;

    public static DailyBalance Open(MerchantId merchantId, DateOnly date, string currency) =>
        new(Guid.CreateVersion7(), merchantId, date, Money.ZeroIn(currency).Currency);

    public void ApplyCredit(Money amount, DateTimeOffset appliedAtUtc)
    {
        EnsureApplicable(amount);

        TotalCredits = TotalCredits.Add(amount);
        CreditCount++;
        Touch(appliedAtUtc);
    }

    public void ApplyDebit(Money amount, DateTimeOffset appliedAtUtc)
    {
        EnsureApplicable(amount);

        TotalDebits = TotalDebits.Add(amount);
        DebitCount++;
        Touch(appliedAtUtc);
    }

    /// <summary>Compensates a cancelled credit. Totals are never allowed to go negative.</summary>
    public void ReverseCredit(Money amount, DateTimeOffset appliedAtUtc)
    {
        EnsureApplicable(amount);
        EnsureReversible(TotalCredits, amount, CreditCount, "credit");

        TotalCredits = TotalCredits.Subtract(amount);
        CreditCount--;
        Touch(appliedAtUtc);
    }

    /// <summary>Compensates a cancelled debit.</summary>
    public void ReverseDebit(Money amount, DateTimeOffset appliedAtUtc)
    {
        EnsureApplicable(amount);
        EnsureReversible(TotalDebits, amount, DebitCount, "debit");

        TotalDebits = TotalDebits.Subtract(amount);
        DebitCount--;
        Touch(appliedAtUtc);
    }

    private void Touch(DateTimeOffset appliedAtUtc)
    {
        LastUpdatedAtUtc = appliedAtUtc;
        Version++;
    }

    private void EnsureApplicable(Money amount)
    {
        if (!amount.IsPositive)
        {
            throw new DomainException(
                "daily_balance.amount_not_positive",
                "Only positive amounts can be applied to a daily balance.");
        }

        if (!string.Equals(amount.Currency, Currency, StringComparison.Ordinal))
        {
            throw new DomainException(
                "daily_balance.currency_mismatch",
                $"Cannot apply {amount.Currency} to a balance kept in {Currency}.");
        }
    }

    private static void EnsureReversible(Money total, Money amount, int count, string kind)
    {
        if (count == 0 || total < amount)
        {
            throw new DomainException(
                "daily_balance.reversal_exceeds_total",
                $"Reversing {amount} would drive the {kind} total of the day negative.");
        }
    }
}
