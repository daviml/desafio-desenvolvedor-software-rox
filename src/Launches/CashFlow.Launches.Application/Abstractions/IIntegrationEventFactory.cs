using CashFlow.Messaging;
using CashFlow.SharedKernel.Domain;

namespace CashFlow.Launches.Application.Abstractions;

/// <summary>
/// Translates internal domain events into the public integration contract.
/// This seam is what stops a domain refactoring from becoming a breaking change for consumers.
/// </summary>
public interface IIntegrationEventFactory
{
    /// <summary>Returns <see langword="null"/> for domain events that are purely internal.</summary>
    IntegrationEvent? TryCreate(IDomainEvent domainEvent);
}
