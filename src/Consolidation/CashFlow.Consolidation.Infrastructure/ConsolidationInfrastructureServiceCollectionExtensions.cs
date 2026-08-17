using CashFlow.Consolidation.Application.Abstractions;
using CashFlow.Consolidation.Domain.DailyBalances;
using CashFlow.Consolidation.Infrastructure.Persistence;
using CashFlow.Messaging;
using CashFlow.Messaging.Contracts;
using CashFlow.Messaging.RabbitMq;
using CashFlow.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CashFlow.Consolidation.Infrastructure;

/// <summary>Transport used to receive integration events.</summary>
public enum MessagingProvider
{
    RabbitMq = 0,

    /// <summary>In-process only. Automated tests and single-process demos.</summary>
    InMemory = 1,
}

public static class ConsolidationInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddConsolidationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMessagingContracts();
        services.AddDatabase(configuration);
        services.AddMessagingTransport(configuration);

        services.AddScoped<IDailyBalanceRepository, DailyBalanceRepository>();
        services.AddScoped<IDailyBalanceQueries, DailyBalanceQueries>();
        services.AddScoped<IProcessedEventStore, ProcessedEventStore>();
        services.AddScoped<IUnitOfWork, ConsolidationUnitOfWork>();

        services.AddHostedService<DatabaseInitializer>();

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        // Settings are resolved from IOptions rather than read here, so the provider and the
        // connection string come from the fully composed configuration (including anything a test
        // host or an orchestrator layers on top) instead of a snapshot taken at registration time.
        services.AddDbContext<ConsolidationDbContext>((serviceProvider, builder) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            switch (databaseOptions.Provider)
            {
                case DatabaseProvider.Sqlite:
                    builder.UseSqlite(
                        databaseOptions.ConnectionString,
                        sqlite => sqlite.CommandTimeout(databaseOptions.CommandTimeoutSeconds));
                    break;

                case DatabaseProvider.Postgres:
                default:
                    builder.UseNpgsql(databaseOptions.ConnectionString, npgsql =>
                    {
                        npgsql.EnableRetryOnFailure(databaseOptions.MaxRetryCount);
                        npgsql.CommandTimeout(databaseOptions.CommandTimeoutSeconds);
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", ConsolidationDbContext.Schema);
                    });
                    break;
            }

            if (databaseOptions.EnableSensitiveDataLogging)
            {
                builder.EnableSensitiveDataLogging();
            }
        });

        services.AddHealthChecks()
            .AddDbContextCheck<ConsolidationDbContext>("consolidation-database", tags: ["ready"]);
    }

    private static void AddMessagingTransport(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetValue("Messaging:Provider", MessagingProvider.RabbitMq);

        if (provider == MessagingProvider.InMemory)
        {
            return;
        }

        var queueName = configuration.GetValue<string>($"{RabbitMqOptions.SectionName}:QueueName");

        if (string.IsNullOrWhiteSpace(queueName))
        {
            throw new InvalidOperationException(
                $"'{RabbitMqOptions.SectionName}:QueueName' must be configured for the consolidation service.");
        }

        services.AddRabbitMqConsumer(configuration);

        // Bind to both entry contracts unless the deployment overrides the routing keys.
        services.PostConfigure<RabbitMqOptions>(options =>
        {
            if (options.RoutingKeys.Count == 0)
            {
                options.RoutingKeys.Add(EntryRegisteredIntegrationEvent.WireName);
                options.RoutingKeys.Add(EntryCancelledIntegrationEvent.WireName);
            }
        });

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready", "broker"]);
    }
}
