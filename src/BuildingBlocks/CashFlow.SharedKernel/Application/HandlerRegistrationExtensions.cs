using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CashFlow.SharedKernel.Application;

/// <summary>
/// Composition root helpers. Decoration is wired explicitly here rather than by assembly scanning,
/// so the pipeline that surrounds every use case is readable in one place.
/// </summary>
public static class HandlerRegistrationExtensions
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> wrapped in the standard pipeline:
    /// logging -> validation -> handler.
    /// </summary>
    public static IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(
        this IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        services.AddScoped<THandler>();
        services.AddScoped<IRequestHandler<TRequest, TResponse>>(serviceProvider =>
        {
            IRequestHandler<TRequest, TResponse> handler = serviceProvider.GetRequiredService<THandler>();

            handler = new ValidationDecorator<TRequest, TResponse>(
                handler,
                serviceProvider.GetServices<IValidator<TRequest>>());

            return new LoggingDecorator<TRequest, TResponse>(
                handler,
                serviceProvider.GetRequiredService<ILogger<LoggingDecorator<TRequest, TResponse>>>());
        });

        return services;
    }
}
