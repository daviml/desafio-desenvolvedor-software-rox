namespace CashFlow.SharedKernel.Results;

/// <summary>
/// A machine-readable failure. <paramref name="Code"/> is stable and safe to branch on;
/// <paramref name="Message"/> is human-facing.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Unexpected);

    /// <summary>Field-level failures, keyed by property name. Empty for non-validation errors.</summary>
    public IReadOnlyDictionary<string, string[]> Details { get; init; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Validation(IReadOnlyDictionary<string, string[]> details) =>
        new("validation.failed", "One or more validation errors occurred.", ErrorType.Validation)
        {
            Details = details,
        };

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unprocessable(string code, string message) => new(code, message, ErrorType.Unprocessable);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);

    public static Error Unexpected(string code, string message) => new(code, message, ErrorType.Unexpected);
}
