using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CashFlow.Messaging.RabbitMq;

/// <summary>
/// Pools publisher channels. Opening a channel per message would add a broker round-trip to every
/// publish; sharing one channel across threads would serialise them. A small pool gives both
/// throughput and thread safety.
/// </summary>
public sealed class RabbitMqPublisherChannelPool(
    RabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqPublisherChannelPool> logger) : IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly ConcurrentBag<IChannel> _idleChannels = [];
    private readonly SemaphoreSlim _capacity = new(options.Value.PublisherChannelPoolSize);
    private bool _topologyDeclared;
    private bool _disposed;

    public async ValueTask<IChannel> RentAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _capacity.WaitAsync(cancellationToken);

        try
        {
            while (_idleChannels.TryTake(out var pooled))
            {
                if (pooled.IsOpen)
                {
                    return pooled;
                }

                await DisposeChannelAsync(pooled);
            }

            return await CreateChannelAsync(cancellationToken);
        }
        catch
        {
            _capacity.Release();
            throw;
        }
    }

    public void Return(IChannel channel)
    {
        if (_disposed || !channel.IsOpen)
        {
            _ = DisposeChannelAsync(channel);
        }
        else
        {
            _idleChannels.Add(channel);
        }

        _capacity.Release();
    }

    private async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(cancellationToken);

        // Publisher confirmations turn "fire and forget" into "the broker persisted it":
        // the outbox only marks a message as dispatched after the broker acknowledges it.
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        if (!_topologyDeclared)
        {
            await RabbitMqTopology.DeclarePublisherTopologyAsync(channel, _options, cancellationToken);
            _topologyDeclared = true;
        }

        return channel;
    }

    private async Task DisposeChannelAsync(IChannel channel)
    {
        try
        {
            await channel.CloseAsync();
            await channel.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Ignoring error while discarding a broken RabbitMQ channel");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        while (_idleChannels.TryTake(out var channel))
        {
            await DisposeChannelAsync(channel);
        }

        _capacity.Dispose();
    }
}
