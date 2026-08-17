using CashFlow.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CashFlow.Web;

/// <summary>
/// Single translation point from application outcomes to HTTP. Endpoints never build status codes
/// by hand, so error semantics stay identical across every route in both services.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<TValue>(
        this Result<TValue> result,
        Func<TValue, IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value)
            : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Type == ErrorType.Validation && error.Details.Count > 0)
        {
            return Results.ValidationProblem(
                error.Details,
                detail: error.Message,
                title: "One or more validation errors occurred.",
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }

        return Results.Problem(
            detail: error.Message,
            statusCode: StatusCodeFor(error.Type),
            title: TitleFor(error.Type),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static int StatusCodeFor(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => "Invalid request",
        ErrorType.NotFound => "Resource not found",
        ErrorType.Conflict => "Conflicting state",
        ErrorType.Unprocessable => "Business rule violated",
        ErrorType.Unavailable => "Service unavailable",
        _ => "Unexpected error",
    };
}
