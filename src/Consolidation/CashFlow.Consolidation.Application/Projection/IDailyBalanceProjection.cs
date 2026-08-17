using CashFlow.Messaging.Contracts;

namespace CashFlow.Consolidation.Application.Projection;

/// <summary>
/// Folds an entry event into the consolidated daily balance.
/// </summary>
/// <remarks>
/// The contract promises the effect is applied exactly once, whatever the transport does.
/// Which implementation the handlers get - the plain projector or the retrying decorator - is a
/// composition-root decision they never see.
/// </remarks>
public interface IDailyBalanceProjection
{
    Task ApplyAsync(EntryRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken);

    Task ApplyAsync(EntryCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
