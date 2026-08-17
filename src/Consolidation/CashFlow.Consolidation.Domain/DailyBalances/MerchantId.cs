namespace CashFlow.Consolidation.Domain.DailyBalances;

/// <summary>
/// Identifies the merchant a consolidated balance belongs to.
/// </summary>
/// <remarks>
/// Intentionally a separate type from the Launches context's identifier: the two bounded contexts
/// share a contract on the wire, not code. That is what keeps them independently deployable.
/// </remarks>
public readonly record struct MerchantId(Guid Value)
{
    public override string ToString() => Value.ToString();
}
