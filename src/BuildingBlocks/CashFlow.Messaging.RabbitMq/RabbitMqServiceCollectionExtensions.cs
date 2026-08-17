using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CashFlow.Messaging.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    /// <summary>Registers the shared connection and options. Safe to call from publisher and consumer alike.</summary>
    public static IServiceCollection AddRabbitMqCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<RabbitMqConnectionProvider>();
        return services;
    }

    /// <summary>Adds the RabbitMQ implementation of <see cref="IIntegrationEventPublisher"/>.</summary>
    public static IServiceCollection AddRabbitMqPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRabbitMqCore(configuration);
        services.AddSingleton<RabbitMqPublisherChannelPool>();
        services.AddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();
        return services;
    }

    /// <summary>Adds the background consumer that feeds <see cref="IIntegrationEventDispatcher"/>.</summary>
    public static IServiceCollection AddRabbitMqConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRabbitMqCore(configuration);
        services.AddHostedService<RabbitMqIntegrationEventConsumer>();
        return services;
    }
}
