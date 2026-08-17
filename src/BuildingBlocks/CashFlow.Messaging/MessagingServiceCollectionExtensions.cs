using CashFlow.Messaging.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CashFlow.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the transport-agnostic messaging pieces: the contract registry and the
    /// event dispatcher. The concrete transport is added separately (RabbitMQ or in-memory).
    /// </summary>
    public static IServiceCollection AddMessagingContracts(this IServiceCollection services)
    {
        services.TryAddSingleton(CreateRegistry());
        services.TryAddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>();
        return services;
    }

    /// <summary>
    /// The published contract catalogue. Adding an event here is the single step required to make
    /// it routable end to end.
    /// </summary>
    public static IntegrationEventRegistry CreateRegistry() =>
        new IntegrationEventRegistry()
            .Register<EntryRegisteredIntegrationEvent>(EntryRegisteredIntegrationEvent.WireName)
            .Register<EntryCancelledIntegrationEvent>(EntryCancelledIntegrationEvent.WireName);
}
