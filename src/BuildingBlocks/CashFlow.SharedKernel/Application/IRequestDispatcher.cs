using CashFlow.SharedKernel.Results;

namespace CashFlow.SharedKernel.Application;

/// <summary>
/// Entry point from the transport layer (HTTP endpoint, message consumer) into the application layer.
/// Endpoints depend on this abstraction only, never on concrete handlers.
/// </summary>
public interface IRequestDispatcher
{
    Task<Result<TResponse>> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken);
}
