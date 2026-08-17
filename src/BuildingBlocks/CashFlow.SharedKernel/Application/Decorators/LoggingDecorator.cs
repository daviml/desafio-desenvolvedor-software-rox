using System.Diagnostics;
using CashFlow.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace CashFlow.SharedKernel.Application;

/// <summary>
/// Decorator that turns every use case execution into a structured log entry with its duration.
/// A cross-cutting concern kept out of the handlers themselves.
/// </summary>
public sealed class LoggingDecorator<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner,
    ILogger<LoggingDecorator<TRequest, TResponse>> logger) : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;

    public async Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        var timestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.HandleAsync(request, cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            if (result.IsSuccess)
            {
                RequestLog.Handled(logger, RequestName, elapsed);
            }
            else
            {
                RequestLog.Rejected(logger, RequestName, elapsed, result.Error.Code, result.Error.Message);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RequestLog.Faulted(logger, RequestName, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds, exception);
            throw;
        }
    }
}

/// <summary>
/// Compile-time generated log methods: no boxing, no string formatting when the level is disabled.
/// Cheap logging matters on a write path that has to sustain peak traffic.
/// </summary>
internal static partial class RequestLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Request {RequestName} handled in {ElapsedMilliseconds} ms")]
    public static partial void Handled(ILogger logger, string requestName, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Request {RequestName} rejected in {ElapsedMilliseconds} ms: {ErrorCode} - {ErrorMessage}")]
    public static partial void Rejected(
        ILogger logger,
        string requestName,
        double elapsedMilliseconds,
        string errorCode,
        string errorMessage);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Request {RequestName} faulted after {ElapsedMilliseconds} ms")]
    public static partial void Faulted(
        ILogger logger,
        string requestName,
        double elapsedMilliseconds,
        Exception exception);
}
