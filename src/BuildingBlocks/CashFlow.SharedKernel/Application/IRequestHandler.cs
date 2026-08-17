using CashFlow.SharedKernel.Results;

namespace CashFlow.SharedKernel.Application;

/// <summary>
/// Single use case. One handler per request keeps classes small and honours the
/// Single Responsibility and Interface Segregation principles.
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
