namespace CashFlow.Launches.Domain.Entries;

/// <summary>Identifies the merchant that owns a cash flow entry.</summary>
public readonly record struct MerchantId(Guid Value)
{
    public static MerchantId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
