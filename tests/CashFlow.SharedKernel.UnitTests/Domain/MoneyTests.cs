using CashFlow.SharedKernel.Domain;

namespace CashFlow.SharedKernel.UnitTests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void From_RoundsToTheCurrencyMinorUnit()
    {
        var money = Money.From(10.005m);

        money.Amount.ShouldBe(10.00m);
    }

    [Fact]
    public void From_RoundsHalfToEven()
    {
        Money.From(2.675m).Amount.ShouldBe(2.68m);
        Money.From(2.665m).Amount.ShouldBe(2.66m);
    }

    [Fact]
    public void From_NormalisesTheCurrencyCode()
    {
        Money.From(1m, "brl").Currency.ShouldBe("BRL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BR")]
    [InlineData("REAIS")]
    public void From_RejectsInvalidCurrencyCodes(string currency)
    {
        Should.Throw<DomainException>(() => Money.From(1m, currency));
    }

    [Fact]
    public void Add_SumsAmountsOfTheSameCurrency()
    {
        var total = Money.From(10.50m).Add(Money.From(4.25m));

        total.Amount.ShouldBe(14.75m);
        total.Currency.ShouldBe("BRL");
    }

    [Fact]
    public void Subtract_CanProduceANegativeResult()
    {
        var result = Money.From(10m).Subtract(Money.From(25m));

        result.Amount.ShouldBe(-15m);
    }

    [Fact]
    public void Operations_RejectMixedCurrencies()
    {
        var brl = Money.From(10m, "BRL");
        var usd = Money.From(10m, "USD");

        var exception = Should.Throw<DomainException>(() => brl.Add(usd));

        exception.Code.ShouldBe("money.currency_mismatch");
    }

    [Fact]
    public void Comparison_OrdersAmountsOfTheSameCurrency()
    {
        (Money.From(10m) < Money.From(20m)).ShouldBeTrue();
        (Money.From(20m) >= Money.From(20m)).ShouldBeTrue();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        Money.From(10m).ShouldBe(Money.From(10.00m));
        Money.From(10m, "BRL").ShouldNotBe(Money.From(10m, "USD"));
    }

    [Fact]
    public void Negate_FlipsTheSignAndKeepsTheCurrency()
    {
        var negated = Money.From(10m, "USD").Negate();

        negated.Amount.ShouldBe(-10m);
        negated.Currency.ShouldBe("USD");
    }

    [Fact]
    public void ToString_IsCultureInvariant()
    {
        Money.From(1234.5m).ToString().ShouldBe("BRL 1234.50");
    }
}
