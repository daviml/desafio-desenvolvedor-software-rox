namespace CashFlow.SharedKernel.Domain;

/// <summary>Something relevant that happened inside the domain, expressed in past tense.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAtUtc { get; }
}
