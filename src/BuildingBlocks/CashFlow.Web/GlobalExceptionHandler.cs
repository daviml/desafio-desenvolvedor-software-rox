using CashFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CashFlow.Web;

/// <summary>
/// Last line of defence. Anything that escapes a handler is logged with its correlation id and
/// returned as RFC 7807 ProblemDetails - never as a stack trace.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var correlationId = httpContext.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var value)
            ? value?.ToString()
            : httpContext.TraceIdentifier;

        var problem = exception switch
        {
            DomainException domainException => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Business rule violated",
                Detail = domainException.Message,
                Extensions = { ["code"] = domainException.Code },
            },
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = badRequest.Message,
                Extensions = { ["code"] = "request.malformed" },
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected error",
                Detail = "The request could not be processed. Please retry or contact support with the correlation id.",
                Extensions = { ["code"] = "server.unexpected_error" },
            },
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path} (correlation {CorrelationId})",
                httpContext.Request.Method,
                httpContext.Request.Path,
                correlationId);
        }
        else
        {
            logger.LogWarning(
                "Request {Method} {Path} rejected: {Detail} (correlation {CorrelationId})",
                httpContext.Request.Method,
                httpContext.Request.Path,
                problem.Detail,
                correlationId);
        }

        problem.Instance = httpContext.Request.Path;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
