using CashFlow.Launches.Domain.Entries;
using CashFlow.Launches.Infrastructure.Persistence;
using CashFlow.Launches.Infrastructure.Persistence.Outbox;
using CashFlow.Messaging;
using CashFlow.Messaging.InMemory;
using CashFlow.Messaging.RabbitMq;
using CashFlow.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CashFlow.Launches.Infrastructure;

/// <summary>Transport used to publish integration events.</summary>
public enum MessagingProvider
{
    RabbitMq = 0,

    /// <summary>In-process only. Automated tests and single-process demos.</summary>
    InMemory = 1,
}

public static class LaunchesInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Wires the adapters that satisfy the ports declared by the domain and application layers.
    /// Everything here is replaceable; nothing above this layer knows which technology was chosen.
    /// </summary>
    public static IServiceCollection AddLaunchesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMessagingContracts();
        services.AddDatabase(configuration);
        services.AddMessagingTransport(configuration);

        services.AddScoped<IEntryRepository, EntryRepository>();
        services.AddScoped<IEntryQueries, EntryQueries>();
        services.AddScoped<IUnitOfWork, LaunchesUnitOfWork>();
        services.AddScoped<OutboxDispatcher>();

        services.AddHostedService<DatabaseInitializer>();
        services.AddHostedService<OutboxDispatcherHostedService>();

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<OutboxSaveChangesInterceptor>();

        // Settings are resolved from IOptions rather than read here, so the provider and the
        // connection string come from the fully composed configuration (including anything a test
        // host or an orchestrator layers on top) instead of a snapshot taken at registration time.
        services.AddDbContext<LaunchesDbContext>((serviceProvider, builder) =>
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
                        npgsql.MigrationsHistoryTable("__ef_migrations_history", LaunchesDbContext.Schema);
                    });
                    break;
            }

            builder.AddInterceptors(serviceProvider.GetRequiredService<OutboxSaveChangesInterceptor>());

            if (databaseOptions.EnableSensitiveDataLogging)
            {
                builder.EnableSensitiveDataLogging();
            }
        });

        services.AddHealthChecks()
            .AddDbContextCheck<LaunchesDbContext>("launches-database", tags: ["ready"]);
    }

    private static void AddMessagingTransport(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetValue("Messaging:Provider", MessagingProvider.RabbitMq);

        if (provider == MessagingProvider.InMemory)
        {
            services.AddSingleton<IIntegrationEventPublisher, InMemoryIntegrationEventPublisher>();
            return;
        }

        services.AddRabbitMqPublisher(configuration);

        // Reported as "degraded", never "unhealthy": a broker outage must not take this service
        // out of the load balancer - entries keep being accepted and queue in the outbox.
        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>(
                "rabbitmq",
                failureStatus: HealthStatus.Degraded,
                tags: ["broker"]);
    }
}
