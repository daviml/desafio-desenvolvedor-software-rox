using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;

namespace CashFlow.Messaging.RabbitMq;

/// <summary>
/// Owns the single AMQP connection of the process and re-establishes it when the broker goes away.
/// Connections are expensive; channels are not - hence one connection, many channels.
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly ResiliencePipeline _connectPipeline;
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnectionProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connectPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                Delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMilliseconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = arguments =>
                {
                    _logger.LogWarning(
                        arguments.Outcome.Exception,
                        "RabbitMQ connection attempt {AttemptNumber} failed; retrying in {Delay}",
                        arguments.AttemptNumber + 1,
                        arguments.RetryDelay);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    public bool IsConnected => _connection is { IsOpen: true };

    public async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection is { IsOpen: true } current)
        {
            return current;
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } existing)
            {
                return existing;
            }

            if (_connection is not null)
            {
                await SafeCloseAsync(_connection);
                _connection = null;
            }

            _connection = await _connectPipeline.ExecuteAsync(
                async token => await CreateConnectionAsync(token),
                cancellationToken);

            _logger.LogInformation(
                "Connected to RabbitMQ at {HostName}:{Port}{VirtualHost}",
                _options.HostName,
                _options.Port,
                _options.VirtualHost);

            return _connection;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = _options.ClientProvidedName,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds),
            ConsumerDispatchConcurrency = _options.ConsumerConcurrency,
        };

        return factory.CreateConnectionAsync(cancellationToken);
    }

    private async Task SafeCloseAsync(IConnection connection)
    {
        try
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Ignoring error while closing a broken RabbitMQ connection");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_connection is not null)
        {
            await SafeCloseAsync(_connection);
            _connection = null;
        }

        _mutex.Dispose();
    }
}
