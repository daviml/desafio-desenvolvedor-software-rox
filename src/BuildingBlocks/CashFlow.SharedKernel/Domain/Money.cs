using System.Globalization;

namespace CashFlow.SharedKernel.Domain;

/// <summary>
/// Monetary value object. Immutable, currency-aware and rounded to the currency's minor unit,
/// so no layer above has to remember to round or to guard against mixing currencies.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public const string DefaultCurrency = "BRL";
    private const int Scale = 2;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero => new(0m, DefaultCurrency);

    public static Money ZeroIn(string currency) => new(0m, NormalizeCurrency(currency));

    /// <summary>Creates a monetary amount, rejecting NaN-like inputs and unsupported currency codes.</summary>
    public static Money From(decimal amount, string currency = DefaultCurrency)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        return new Money(Round(amount), normalizedCurrency);
    }

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Round(Amount + other.Amount), Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Round(Amount - other.Amount), Currency);
    }

    public Money Negate() => new(Round(-Amount), Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Currency} {Amount:0.00}");

    private static decimal Round(decimal value) => Math.Round(value, Scale, MidpointRounding.ToEven);

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("money.currency_required", "Currency is required.");
        }

        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new DomainException(
                "money.currency_invalid",
                $"Currency '{currency}' is not a valid ISO-4217 three-letter code.");
        }

        return normalized;
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainException(
                "money.currency_mismatch",
                $"Cannot operate on different currencies ('{Currency}' and '{other.Currency}').");
        }
    }
}
