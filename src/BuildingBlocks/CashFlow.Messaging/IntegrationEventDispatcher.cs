using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Messaging;

/// <summary>
/// Resolves the handler registered for a concrete event type and invokes it.
/// Reflection happens once per event type; afterwards it is a cached virtual call.
/// </summary>
public sealed class IntegrationEventDispatcher(IServiceProvider serviceProvider) : IIntegrationEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> InvokerCache = new();

    public Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var invoker = InvokerCache.GetOrAdd(
            integrationEvent.GetType(),
            static eventType => (HandlerInvoker)Activator.CreateInstance(
                typeof(TypedHandlerInvoker<>).MakeGenericType(eventType))!);

        return invoker.InvokeAsync(serviceProvider, integrationEvent, cancellationToken);
    }

    private abstract class HandlerInvoker
    {
        public abstract Task InvokeAsync(
            IServiceProvider serviceProvider,
            IntegrationEvent integrationEvent,
            CancellationToken cancellationToken);
    }

    private sealed class TypedHandlerInvoker<TEvent> : HandlerInvoker
        where TEvent : IntegrationEvent
    {
        public override async Task InvokeAsync(
            IServiceProvider serviceProvider,
            IntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            var handlers = serviceProvider.GetServices<IIntegrationEventHandler<TEvent>>();

            foreach (var handler in handlers)
            {
                await handler.HandleAsync((TEvent)integrationEvent, cancellationToken);
            }
        }
    }
}
