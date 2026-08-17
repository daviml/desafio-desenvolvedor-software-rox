using CashFlow.Consolidation.Application.Abstractions;
using CashFlow.Messaging.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidation.Application.Projection;

/// <summary>
/// Decorator that retries a projection whose transaction lost a write race.
/// </summary>
/// <remarks>
/// Two consumers folding entries of the same merchant and day collide in one of two ways: both
/// try to create the day row, or one commits a new <c>version</c> before the other. Either way the
/// loser must re-read and re-apply - dropping the event would silently lose money from the balance.
/// <para>
/// Each attempt runs in a <em>fresh DI scope</em>, because a failed <c>SaveChanges</c> leaves the
/// previous unit of work holding stale, partially-tracked state.
/// </para>
/// <para>
/// The retry lives here rather than in the RabbitMQ consumer on purpose: the conflict is a property
/// of the projection, so every caller inherits the guarantee - including in-process transports and
/// any future replay tooling.
/// </para>
/// </remarks>
public sealed class RetryingDailyBalanceProjection(
    IServiceScopeFactory scopeFactory,
    ILogger<RetryingDailyBalanceProjection> logger) : IDailyBalanceProjection
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(20);

    public Task ApplyAsync(EntryRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return ExecuteAsync(
            integrationEvent.EventId,
            (projector, token) => projector.ApplyAsync(integrationEvent, token),
            cancellationToken);
    }

    public Task ApplyAsync(EntryCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return ExecuteAsync(
            integrationEvent.EventId,
            (projector, token) => projector.ApplyAsync(integrationEvent, token),
            cancellationToken);
    }

    private async Task ExecuteAsync(
        Guid eventId,
        Func<DailyBalanceProjector, CancellationToken, Task> apply,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var projector = scope.ServiceProvider.GetRequiredService<DailyBalanceProjector>();

            try
            {
                await apply(projector, cancellationToken);
                return;
            }
            catch (ConcurrencyConflictException exception) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    "Event {EventId} lost a write race on attempt {Attempt}; retrying on fresh state: {Reason}",
                    eventId,
                    attempt,
                    exception.Message);

                await Task.Delay(BaseDelay * attempt, cancellationToken);
            }
        }
    }
}
