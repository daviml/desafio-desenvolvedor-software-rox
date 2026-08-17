using CashFlow.Consolidation.Domain.DailyBalances;
using CashFlow.Consolidation.UnitTests.TestSupport;
using CashFlow.SharedKernel.Domain;

namespace CashFlow.Consolidation.UnitTests.Domain;

public sealed class DailyBalanceTests
{
    private static readonly MerchantId Merchant = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly DateOnly Date = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = FixedClock.DefaultNow;

    private static DailyBalance NewBalance() => DailyBalance.Open(Merchant, Date, "BRL");

    [Fact]
    public void Open_StartsAtZero()
    {
        var balance = NewBalance();

        balance.MerchantId.ShouldBe(Merchant);
        balance.Date.ShouldBe(Date);
        balance.Currency.ShouldBe("BRL");
        balance.TotalCredits.IsZero.ShouldBeTrue();
        balance.TotalDebits.IsZero.ShouldBeTrue();
        balance.Balance.IsZero.ShouldBeTrue();
        balance.EntryCount.ShouldBe(0);
        balance.Version.ShouldBe(0);
    }

    [Fact]
    public void ApplyCredit_IncreasesCreditsAndTheBalance()
    {
        var balance = NewBalance();

        balance.ApplyCredit(Money.From(100m), Now);
        balance.ApplyCredit(Money.From(49.50m), Now);

        balance.TotalCredits.Amount.ShouldBe(149.50m);
        balance.CreditCount.ShouldBe(2);
        balance.Balance.Amount.ShouldBe(149.50m);
        balance.LastUpdatedAtUtc.ShouldBe(Now);
        balance.Version.ShouldBe(2);
    }

    [Fact]
    public void ApplyDebit_DecreasesTheBalance()
    {
        var balance = NewBalance();

        balance.ApplyCredit(Money.From(100m), Now);
        balance.ApplyDebit(Money.From(30m), Now);

        balance.TotalDebits.Amount.ShouldBe(30m);
        balance.DebitCount.ShouldBe(1);
        balance.Balance.Amount.ShouldBe(70m);
        balance.EntryCount.ShouldBe(2);
    }

    [Fact]
    public void Balance_CanBeNegativeWhenDebitsExceedCredits()
    {
        var balance = NewBalance();

        balance.ApplyCredit(Money.From(10m), Now);
        balance.ApplyDebit(Money.From(35m), Now);

        balance.Balance.Amount.ShouldBe(-25m);
    }

    [Fact]
    public void ReverseCredit_CompensatesAPreviouslyAppliedCredit()
    {
        var balance = NewBalance();
        balance.ApplyCredit(Money.From(100m), Now);

        balance.ReverseCredit(Money.From(100m), Now);

        balance.TotalCredits.IsZero.ShouldBeTrue();
        balance.CreditCount.ShouldBe(0);
        balance.Balance.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void ReverseDebit_CompensatesAPreviouslyAppliedDebit()
    {
        var balance = NewBalance();
        balance.ApplyCredit(Money.From(100m), Now);
        balance.ApplyDebit(Money.From(40m), Now);

        balance.ReverseDebit(Money.From(40m), Now);

        balance.TotalDebits.IsZero.ShouldBeTrue();
        balance.DebitCount.ShouldBe(0);
        balance.Balance.Amount.ShouldBe(100m);
    }

    [Fact]
    public void ReverseCredit_BeyondTheAppliedTotal_IsRejected()
    {
        var balance = NewBalance();
        balance.ApplyCredit(Money.From(50m), Now);

        var exception = Should.Throw<DomainException>(() => balance.ReverseCredit(Money.From(80m), Now));

        exception.Code.ShouldBe("daily_balance.reversal_exceeds_total");
        balance.TotalCredits.Amount.ShouldBe(50m);
    }

    [Fact]
    public void ReverseDebit_OnADayWithoutDebits_IsRejected()
    {
        var balance = NewBalance();

        Should.Throw<DomainException>(() => balance.ReverseDebit(Money.From(10m), Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Apply_RejectsAmountsThatAreNotPositive(decimal amount)
    {
        var balance = NewBalance();

        var exception = Should.Throw<DomainException>(() => balance.ApplyCredit(Money.From(amount), Now));

        exception.Code.ShouldBe("daily_balance.amount_not_positive");
    }

    [Fact]
    public void Apply_RejectsAForeignCurrency()
    {
        var balance = NewBalance();

        var exception = Should.Throw<DomainException>(() => balance.ApplyCredit(Money.From(10m, "USD"), Now));

        exception.Code.ShouldBe("daily_balance.currency_mismatch");
    }

    [Fact]
    public void Version_IncrementsOnEveryChangeSoConcurrentWritersConflict()
    {
        var balance = NewBalance();

        balance.ApplyCredit(Money.From(1m), Now);
        balance.ApplyDebit(Money.From(1m), Now);
        balance.ReverseDebit(Money.From(1m), Now);

        balance.Version.ShouldBe(3);
    }
}
