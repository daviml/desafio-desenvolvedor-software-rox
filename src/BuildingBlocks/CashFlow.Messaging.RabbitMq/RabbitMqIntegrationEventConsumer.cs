using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CashFlow.Messaging.RabbitMq;

/// <summary>
/// Long-running consumer. Messages are acknowledged only after a handler succeeds, so a crash
/// mid-processing means redelivery rather than a lost financial fact.
/// </summary>
/// <remarks>
/// Failure policy:
/// <list type="bullet">
///   <item>transient failure - retried in process with exponential backoff and jitter;</item>
///   <item>still failing / undeserialisable - rejected without requeue, which routes it to the
///   dead-letter queue for inspection instead of hot-looping the broker;</item>
///   <item>broker unavailable - the loop backs off and re-establishes the topology.</item>
/// </list>
/// </remarks>
public sealed class RabbitMqIntegrationEventConsumer(
    RabbitMqConnectionProvider connectionProvider,
    IntegrationEventRegistry registry,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqIntegrationEventConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ChannelHealthPollInterval = TimeSpan.FromSeconds(5);

    private readonly RabbitMqOptions _options = options.Value;
    private readonly ResiliencePipeline _processingPipeline = BuildProcessingPipeline(options.Value, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilChannelClosesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Consumer for queue {QueueName} stopped unexpectedly; reconnecting in {Delay}",
                    _options.QueueName,
                    ReconnectDelay);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task ConsumeUntilChannelClosesAsync(CancellationToken stoppingToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareConsumerTopologyAsync(channel, _options, stoppingToken);

        // Bound the number of unacknowledged messages so one replica cannot hoard the backlog.
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _options.PrefetchCount,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, deliverEventArgs) =>
            OnMessageReceivedAsync(channel, deliverEventArgs, stoppingToken);

        var consumerTag = await channel.BasicConsumeAsync(
            queue: _options.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "Consuming queue {QueueName} (consumerTag {ConsumerTag}, prefetch {PrefetchCount})",
            _options.QueueName,
            consumerTag,
            _options.PrefetchCount);

        while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
        {
            await Task.Delay(ChannelHealthPollInterval, stoppingToken);
        }
    }

    private async Task OnMessageReceivedAsync(
        IChannel channel,
        BasicDeliverEventArgs deliverEventArgs,
        CancellationToken cancellationToken)
    {
        var wireName = deliverEventArgs.BasicProperties.Type ?? deliverEventArgs.RoutingKey;

        if (!registry.TryResolveType(wireName, out var eventType))
        {
            logger.LogError(
                "Received unknown event type {WireName}; dead-lettering message {MessageId}",
                wireName,
                deliverEventArgs.BasicProperties.MessageId);
            await RejectAsync(channel, deliverEventArgs, cancellationToken);
            return;
        }

        IntegrationEvent integrationEvent;
        try
        {
            integrationEvent = IntegrationEventSerializer.Deserialize(deliverEventArgs.Body.Span, eventType);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Malformed payload for {WireName}; dead-lettering message {MessageId}",
                wireName,
                deliverEventArgs.BasicProperties.MessageId);
            await RejectAsync(channel, deliverEventArgs, cancellationToken);
            return;
        }

        try
        {
            await _processingPipeline.ExecuteAsync(
                async token =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
                    await dispatcher.DispatchAsync(integrationEvent, token);
                },
                cancellationToken);

            await channel.BasicAckAsync(deliverEventArgs.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: leave the message unacknowledged so the broker redelivers it.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Handling {WireName} {EventId} failed after {Attempts} attempts; dead-lettering",
                wireName,
                integrationEvent.EventId,
                _options.MaxProcessingAttempts);
            await RejectAsync(channel, deliverEventArgs, cancellationToken);
        }
    }

    private async Task RejectAsync(
        IChannel channel,
        BasicDeliverEventArgs deliverEventArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.BasicNackAsync(
                deliverEventArgs.DeliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not dead-letter message {DeliveryTag}", deliverEventArgs.DeliveryTag);
        }
    }

    private static ResiliencePipeline BuildProcessingPipeline(RabbitMqOptions options, ILogger logger) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, options.MaxProcessingAttempts - 1),
                Delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMilliseconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = arguments =>
                {
                    logger.LogWarning(
                        arguments.Outcome.Exception,
                        "Message processing attempt {AttemptNumber} failed; retrying in {Delay}",
                        arguments.AttemptNumber + 1,
                        arguments.RetryDelay);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
}
