using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CashFlow.Messaging.RabbitMq;

/// <summary>
/// Publishes integration events to the topic exchange using the event's wire name as routing key,
/// with persistent delivery and publisher confirmations.
/// </summary>
public sealed class RabbitMqIntegrationEventPublisher(
    RabbitMqPublisherChannelPool channelPool,
    IntegrationEventRegistry registry,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    private readonly RabbitMqOptions _options = options.Value;

    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var routingKey = registry.GetWireName(integrationEvent.GetType());
        var body = JsonSerializer.SerializeToUtf8Bytes(
            integrationEvent,
            integrationEvent.GetType(),
            IntegrationEventSerializer.Options);

        var properties = new BasicProperties
        {
            MessageId = integrationEvent.EventId.ToString(),
            Type = routingKey,
            ContentType = "application/json",
            ContentEncoding = Encoding.UTF8.WebName,
            DeliveryMode = DeliveryModes.Persistent,
            CorrelationId = integrationEvent.CorrelationId,
            Timestamp = new AmqpTimestamp(integrationEvent.OccurredAtUtc.ToUnixTimeSeconds()),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));

        var channel = await channelPool.RentAsync(timeout.Token);
        try
        {
            // mandatory: an unroutable event must fail loudly so the outbox retries it later
            // instead of the broker silently discarding a financial fact.
            await channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: timeout.Token);

            logger.LogDebug(
                "Published {RoutingKey} {EventId} to exchange {Exchange}",
                routingKey,
                integrationEvent.EventId,
                _options.Exchange);
        }
        finally
        {
            channelPool.Return(channel);
        }
    }
}
