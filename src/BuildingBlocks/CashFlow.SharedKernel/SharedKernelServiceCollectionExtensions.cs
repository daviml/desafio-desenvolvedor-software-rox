using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CashFlow.SharedKernel;

public static class SharedKernelServiceCollectionExtensions
{
    /// <summary>Registers the building blocks every service needs: clock and request dispatcher.</summary>
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<IRequestDispatcher, RequestDispatcher>();
        return services;
    }
}
