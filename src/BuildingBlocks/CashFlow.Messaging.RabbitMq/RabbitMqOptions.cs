using System.ComponentModel.DataAnnotations;

namespace CashFlow.Messaging.RabbitMq;

/// <summary>Broker settings, bound from the "Messaging:RabbitMq" configuration section.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "Messaging:RabbitMq";

    [Required]
    public string HostName { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    [Required]
    public string UserName { get; set; } = "guest";

    [Required]
    public string Password { get; set; } = "guest";

    [Required]
    public string VirtualHost { get; set; } = "/";

    /// <summary>Topic exchange every integration event is published to.</summary>
    [Required]
    public string Exchange { get; set; } = "cashflow.events";

    /// <summary>Exchange that collects messages which could not be processed.</summary>
    [Required]
    public string DeadLetterExchange { get; set; } = "cashflow.events.dlx";

    /// <summary>Durable queue this service consumes from. Empty for publish-only services.</summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>Routing keys bound to <see cref="QueueName"/>. Wildcards ("cashflow.entry.*") are allowed.</summary>
    public IList<string> RoutingKeys { get; } = [];

    /// <summary>
    /// Unacknowledged messages allowed in flight per consumer. Bounds memory use and spreads
    /// load evenly when several replicas consume the same queue.
    /// </summary>
    [Range(1, 65535)]
    public ushort PrefetchCount { get; set; } = 64;

    /// <summary>Messages dispatched concurrently by the client. Raise to scale a single replica.</summary>
    [Range(1, 256)]
    public ushort ConsumerConcurrency { get; set; } = 8;

    /// <summary>In-process attempts before a message is dead-lettered.</summary>
    [Range(1, 10)]
    public int MaxProcessingAttempts { get; set; } = 3;

    [Range(1, 60_000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    [Range(1, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 10;

    [Range(1, 300)]
    public int PublishTimeoutSeconds { get; set; } = 10;

    /// <summary>Upper bound of pooled publisher channels. Publishing is CPU-cheap; a small pool is enough.</summary>
    [Range(1, 64)]
    public int PublisherChannelPoolSize { get; set; } = 8;

    public string ClientProvidedName { get; set; } = "cashflow";
}
