using CashFlow.SharedKernel.Results;

namespace CashFlow.Launches.Application.Entries;

/// <summary>
/// Single catalogue of the failures this context can return. Keeping them together makes the
/// API surface predictable and gives clients stable codes to branch on.
/// </summary>
public static class EntryErrors
{
    public static Error NotFound(Guid entryId) =>
        Error.NotFound("entry.not_found", $"Entry '{entryId}' was not found.");

    public static Error AlreadyCancelled(Guid entryId) =>
        Error.Conflict("entry.already_cancelled", $"Entry '{entryId}' has already been cancelled.");

    public static Error Rejected(string code, string message) => Error.Unprocessable(code, message);
}
