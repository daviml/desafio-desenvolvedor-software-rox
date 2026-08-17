using CashFlow.SharedKernel.Results;
using FluentValidation;

namespace CashFlow.SharedKernel.Application;

/// <summary>
/// Decorator that runs every registered validator before the inner handler.
/// Handlers therefore never start with a wall of argument checks (Open/Closed:
/// validation is added around a handler, not inside it).
/// </summary>
public sealed class ValidationDecorator<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner,
    IEnumerable<IValidator<TRequest>> validators) : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IValidator<TRequest>[] _validators = [.. validators];

    public async Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        if (_validators.Length == 0)
        {
            return await inner.HandleAsync(request, cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            if (!result.IsValid)
            {
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count > 0)
        {
            var details = failures
                .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(failure => failure.ErrorMessage).ToArray(),
                    StringComparer.Ordinal);

            return Result<TResponse>.Failure(Error.Validation(details));
        }

        return await inner.HandleAsync(request, cancellationToken);
    }
}
