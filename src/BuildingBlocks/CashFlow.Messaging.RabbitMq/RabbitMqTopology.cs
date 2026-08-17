using RabbitMQ.Client;

namespace CashFlow.Messaging.RabbitMq;

/// <summary>
/// Declares the exchange/queue layout. Declarations are idempotent, so every process can assert
/// the topology it depends on at start-up and the services stay independently deployable.
/// </summary>
internal static class RabbitMqTopology
{
    public const string DeadLetterQueueSuffix = ".dlq";

    public static async Task DeclarePublisherTopologyAsync(
        IChannel channel,
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: options.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: options.DeadLetterExchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
    }

    public static async Task DeclareConsumerTopologyAsync(
        IChannel channel,
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        await DeclarePublisherTopologyAsync(channel, options, cancellationToken);

        if (string.IsNullOrWhiteSpace(options.QueueName))
        {
            throw new InvalidOperationException(
                $"'{RabbitMqOptions.SectionName}:QueueName' must be configured for a consuming service.");
        }

        var deadLetterQueue = options.QueueName + DeadLetterQueueSuffix;

        // Messages rejected after all processing attempts land here for manual inspection/replay
        // instead of being silently dropped or looping forever.
        await channel.QueueDeclareAsync(
            queue: deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: deadLetterQueue,
            exchange: options.DeadLetterExchange,
            routingKey: "#",
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = options.DeadLetterExchange,
            },
            cancellationToken: cancellationToken);

        foreach (var routingKey in options.RoutingKeys)
        {
            await channel.QueueBindAsync(
                queue: options.QueueName,
                exchange: options.Exchange,
                routingKey: routingKey,
                cancellationToken: cancellationToken);
        }
    }
}
