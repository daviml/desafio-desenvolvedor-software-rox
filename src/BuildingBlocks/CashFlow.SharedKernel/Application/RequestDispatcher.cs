using System.Collections.Concurrent;
using CashFlow.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.SharedKernel.Application;

/// <summary>
/// Minimal in-process mediator. Deliberately hand-rolled instead of pulling in a
/// full mediator library: the whole contract is ~40 lines, has no licensing strings attached
/// and resolves each request type reflectively only once (then it is a cached virtual call),
/// which matters on the hot write path.
/// </summary>
public sealed class RequestDispatcher(IServiceProvider serviceProvider) : IRequestDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> InvokerCache = new();

    public Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invoker = (HandlerInvoker<TResponse>)InvokerCache.GetOrAdd(
            request.GetType(),
            static requestType => CreateInvoker<TResponse>(requestType));

        return invoker.InvokeAsync(serviceProvider, request, cancellationToken);
    }

    private static object CreateInvoker<TResponse>(Type requestType)
    {
        var invokerType = typeof(TypedHandlerInvoker<,>).MakeGenericType(requestType, typeof(TResponse));
        return Activator.CreateInstance(invokerType)!;
    }

    private abstract class HandlerInvoker<TResponse>
    {
        public abstract Task<Result<TResponse>> InvokeAsync(
            IServiceProvider serviceProvider,
            object request,
            CancellationToken cancellationToken);
    }

    private sealed class TypedHandlerInvoker<TRequest, TResponse> : HandlerInvoker<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<Result<TResponse>> InvokeAsync(
            IServiceProvider serviceProvider,
            object request,
            CancellationToken cancellationToken)
        {
            var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException(
                    $"No handler registered for request '{typeof(TRequest).Name}'.");

            return handler.HandleAsync((TRequest)request, cancellationToken);
        }
    }
}
