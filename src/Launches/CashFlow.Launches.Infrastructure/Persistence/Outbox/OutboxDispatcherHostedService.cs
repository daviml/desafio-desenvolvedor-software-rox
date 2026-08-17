using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CashFlow.Launches.Infrastructure.Persistence.Outbox;

/// <summary>
/// Drives <see cref="OutboxDispatcher"/> on a loop. Runs inside the Launches API process for
/// simplicity; nothing in its design prevents it from being deployed as a separate worker,
/// which is the natural next step when the write path needs to scale independently.
/// </summary>
internal sealed class OutboxDispatcherHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcherHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogWarning("Outbox dispatcher is disabled by configuration");
            return;
        }

        var pollInterval = TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds);
        var cooldown = TimeSpan.FromMilliseconds(_options.CircuitBreakerCooldownMilliseconds);
        var consecutiveFailures = 0;
        var nextPurge = DateTimeOffset.MinValue;

        logger.LogInformation(
            "Outbox dispatcher started (batch {BatchSize}, poll {PollInterval})",
            _options.BatchSize,
            pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            var idle = true;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

                var result = await dispatcher.SweepAsync(stoppingToken);

                if (result.Failed > 0)
                {
                    consecutiveFailures++;
                }
                else
                {
                    consecutiveFailures = 0;
                }

                // A full batch means the queue is still backed up: keep going without sleeping.
                idle = !result.HasMoreWork || result.Read < _options.BatchSize;

                if (DateTimeOffset.UtcNow >= nextPurge)
                {
                    var purged = await dispatcher.PurgeProcessedAsync(stoppingToken);
                    nextPurge = DateTimeOffset.UtcNow.Add(PurgeInterval);

                    if (purged > 0)
                    {
                        logger.LogInformation("Purged {PurgedCount} processed outbox messages", purged);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                logger.LogError(exception, "Outbox sweep failed");
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Simple circuit breaker: after repeated failures the broker (or the database) is
            // clearly unavailable, so back off hard instead of hammering it. The API keeps
            // accepting entries the whole time - they just queue up in the outbox table.
            if (consecutiveFailures >= _options.CircuitBreakerFailureThreshold)
            {
                logger.LogWarning(
                    "Outbox dispatcher paused for {Cooldown} after {FailureCount} consecutive failures",
                    cooldown,
                    consecutiveFailures);

                await SafeDelayAsync(cooldown, stoppingToken);
                consecutiveFailures = 0;
            }
            else if (idle)
            {
                await SafeDelayAsync(pollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Outbox dispatcher stopped");
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
