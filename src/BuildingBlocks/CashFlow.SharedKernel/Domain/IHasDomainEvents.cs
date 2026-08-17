namespace CashFlow.SharedKernel.Domain;

/// <summary>
/// Non-generic view over an aggregate's pending domain events, so infrastructure can collect them
/// without knowing the aggregate's identifier type.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
