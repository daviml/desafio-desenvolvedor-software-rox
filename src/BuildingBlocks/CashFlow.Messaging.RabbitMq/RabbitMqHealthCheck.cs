using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CashFlow.Messaging.RabbitMq;

/// <summary>
/// Reports broker reachability. Registered as a "readiness" check only: the Launches API must stay
/// live and keep accepting entries even while the broker is down (the outbox absorbs the outage).
/// </summary>
public sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connectionProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);

            return connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
                : HealthCheckResult.Degraded("RabbitMQ connection is closed.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", exception);
        }
    }
}
