using CashFlow.Messaging;
using CashFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CashFlow.Launches.Infrastructure.Persistence.Outbox;

/// <summary>
/// Moves pending outbox rows to the broker. Extracted from the hosted service so the sweep logic
/// can be exercised directly by tests, without a host or a timer.
/// </summary>
internal sealed class OutboxDispatcher(
    LaunchesDbContext context,
    IIntegrationEventPublisher publisher,
    IntegrationEventRegistry registry,
    IClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    private readonly OutboxOptions _options = options.Value;

    /// <summary>
    /// Publishes at most one batch. Returns how many messages were published successfully.
    /// </summary>
    /// <remarks>
    /// Delivery is at-least-once: a crash between "broker acknowledged" and "row marked processed"
    /// re-sends the message. Consumers deduplicate by event id, which turns that into
    /// exactly-once *effect* - the property that actually matters for a balance.
    /// </remarks>
    public async Task<OutboxSweepResult> SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var pending = await context.OutboxMessages
            .Where(message =>
                message.ProcessedAtUtc == null
                && message.AttemptCount < _options.MaxAttempts
                && (message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= now))
            .OrderBy(message => message.OccurredAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return OutboxSweepResult.Empty;
        }

        var published = 0;
        var failed = 0;

        foreach (var message in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var integrationEvent = Rehydrate(message);
                await publisher.PublishAsync(integrationEvent, cancellationToken);
                message.MarkPublished(clock.UtcNow);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                message.MarkFailed(clock.UtcNow, BackoffFor(message.AttemptCount + 1), exception.Message);
                failed++;

                logger.LogWarning(
                    exception,
                    "Outbox message {MessageId} ({Type}) failed on attempt {AttemptCount}; next attempt at {NextAttemptAtUtc}",
                    message.Id,
                    message.Type,
                    message.AttemptCount,
                    message.NextAttemptAtUtc);

                // The broker is very likely down for the whole batch: stop early instead of
                // burning attempts on every remaining message.
                break;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return new OutboxSweepResult(pending.Count, published, failed);
    }

    /// <summary>Deletes published rows past their retention window so the hot index stays small.</summary>
    public async Task<int> PurgeProcessedAsync(CancellationToken cancellationToken)
    {
        var threshold = clock.UtcNow.AddDays(-_options.RetentionDays);

        return await context.OutboxMessages
            .Where(message => message.ProcessedAtUtc != null && message.ProcessedAtUtc < threshold)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IntegrationEvent Rehydrate(OutboxMessage message)
    {
        if (!registry.TryResolveType(message.Type, out var eventType))
        {
            throw new InvalidOperationException(
                $"Outbox message {message.Id} references unknown contract '{message.Type}'.");
        }

        return IntegrationEventSerializer.Deserialize(message.Payload, eventType);
    }

    private TimeSpan BackoffFor(int attemptCount)
    {
        var exponential = _options.BaseBackoffMilliseconds * Math.Pow(2, Math.Min(attemptCount - 1, 16));
        var capped = Math.Min(exponential, _options.MaxBackoffMilliseconds);

        // Jitter keeps many replicas from retrying in lockstep after a broker outage.
        var jitter = Random.Shared.NextDouble() * 0.2 * capped;

        return TimeSpan.FromMilliseconds(capped + jitter);
    }
}

/// <summary>Outcome of a single outbox sweep.</summary>
internal readonly record struct OutboxSweepResult(int Read, int Published, int Failed)
{
    public static OutboxSweepResult Empty => new(0, 0, 0);

    public bool HasMoreWork => Read > 0 && Failed == 0;
}
