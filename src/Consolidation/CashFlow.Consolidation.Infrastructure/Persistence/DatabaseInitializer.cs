using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>Brings the projection schema up to date at start-up, waiting for the database to accept connections.</summary>
internal sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    private readonly DatabaseOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ApplySchemaOnStartup)
        {
            logger.LogInformation("Schema initialisation skipped by configuration");
            return;
        }

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 10,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Constant,
                OnRetry = arguments =>
                {
                    logger.LogWarning(
                        "Database not ready yet (attempt {AttemptNumber}): {Message}",
                        arguments.AttemptNumber + 1,
                        arguments.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();

        await pipeline.ExecuteAsync(
            async token =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

                if (_options.Provider == DatabaseProvider.Sqlite)
                {
                    await context.Database.EnsureCreatedAsync(token);
                }
                else
                {
                    await context.Database.MigrateAsync(token);
                }

                logger.LogInformation("Consolidation schema is up to date ({Provider})", _options.Provider);
            },
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
