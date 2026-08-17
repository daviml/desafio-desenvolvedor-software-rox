namespace CashFlow.Launches.Application.Abstractions;

/// <summary>
/// Thrown by the persistence layer when two concurrent requests carry the same idempotency key.
/// Translating the database-specific unique-violation here keeps EF Core types out of the
/// application layer while still letting the use case react to the race.
/// </summary>
public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException() : base("Duplicate idempotency key.") =>
        IdempotencyKey = string.Empty;

    public DuplicateIdempotencyKeyException(string message) : base(message) => IdempotencyKey = string.Empty;

    public DuplicateIdempotencyKeyException(string message, Exception innerException)
        : base(message, innerException) => IdempotencyKey = string.Empty;

    private DuplicateIdempotencyKeyException(string message, string idempotencyKey, Exception innerException)
        : base(message, innerException) => IdempotencyKey = idempotencyKey;

    public string IdempotencyKey { get; }

    public static DuplicateIdempotencyKeyException ForKey(string idempotencyKey, Exception innerException) =>
        new($"An entry with idempotency key '{idempotencyKey}' already exists.", idempotencyKey, innerException);
}
