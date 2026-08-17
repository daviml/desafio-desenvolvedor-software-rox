using CashFlow.Launches.Domain.Entries.Events;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Time;

namespace CashFlow.Launches.Domain.Entries;

/// <summary>
/// A single credit or debit in a merchant's cash flow. Aggregate root and the only place where
/// the rules of a financial entry live, so no caller can create an inconsistent record.
/// </summary>
public sealed class Entry : AggregateRoot<EntryId>
{
    public const int MaxDescriptionLength = 200;
    public const int MaxCategoryLength = 60;
    public const int MaxIdempotencyKeyLength = 128;

    /// <summary>How far back an entry may be dated. Older facts belong to a closed accounting period.</summary>
    public const int MaxBackdatingDays = 365;

    private Entry(
        EntryId id,
        MerchantId merchantId,
        EntryType type,
        Money amount,
        DateOnly entryDate,
        string description,
        string? category,
        string? idempotencyKey,
        DateTimeOffset registeredAtUtc) : base(id)
    {
        MerchantId = merchantId;
        Type = type;
        Amount = amount;
        EntryDate = entryDate;
        Description = description;
        Category = category;
        IdempotencyKey = idempotencyKey;
        RegisteredAtUtc = registeredAtUtc;
        Status = EntryStatus.Active;
    }

    /// <summary>Required by EF Core to materialise instances.</summary>
    private Entry()
    {
        Description = string.Empty;
    }

    public MerchantId MerchantId { get; private set; }

    public EntryType Type { get; private set; }

    /// <summary>Always positive. <see cref="Type"/> determines whether it adds to or subtracts from the balance.</summary>
    public Money Amount { get; private set; }

    /// <summary>Business day the entry belongs to - the grouping key of the daily report.</summary>
    public DateOnly EntryDate { get; private set; }

    public string Description { get; private set; }

    public string? Category { get; private set; }

    public EntryStatus Status { get; private set; }

    /// <summary>Client-supplied key that makes a retried registration return the original entry.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset RegisteredAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public bool IsCancelled => Status == EntryStatus.Cancelled;

    /// <summary>The amount as it affects the balance: positive for credits, negative for debits.</summary>
    public Money SignedAmount => Type == EntryType.Credit ? Amount : Amount.Negate();

    /// <summary>
    /// Creates a valid entry or throws. Every invariant of a financial record is asserted here.
    /// </summary>
    public static Entry Register(
        MerchantId merchantId,
        EntryType type,
        Money amount,
        DateOnly entryDate,
        string description,
        IClock clock,
        string? category = null,
        string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        EnsureKnownType(type);
        EnsurePositiveAmount(amount);
        EnsureUsableEntryDate(entryDate, clock);

        var normalizedDescription = NormalizeDescription(description);
        var normalizedCategory = NormalizeCategory(category);
        var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);

        var entry = new Entry(
            EntryId.New(),
            merchantId,
            type,
            amount,
            entryDate,
            normalizedDescription,
            normalizedCategory,
            normalizedIdempotencyKey,
            clock.UtcNow);

        entry.Raise(new EntryRegisteredDomainEvent(
            entry.Id,
            entry.MerchantId,
            entry.Type,
            entry.Amount,
            entry.EntryDate,
            entry.Description,
            entry.RegisteredAtUtc));

        return entry;
    }

    /// <summary>
    /// Cancels the entry. Idempotent by design would hide mistakes, so cancelling twice is rejected:
    /// the caller must know whether it is undoing something that is still in effect.
    /// </summary>
    public void Cancel(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == EntryStatus.Cancelled)
        {
            throw new DomainException("entry.already_cancelled", $"Entry {Id} has already been cancelled.");
        }

        Status = EntryStatus.Cancelled;
        CancelledAtUtc = clock.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        Raise(new EntryCancelledDomainEvent(
            Id,
            MerchantId,
            Type,
            Amount,
            EntryDate,
            CancellationReason,
            CancelledAtUtc.Value));
    }

    private static void EnsureKnownType(EntryType type)
    {
        if (type is not (EntryType.Credit or EntryType.Debit))
        {
            throw new DomainException("entry.type_invalid", $"'{type}' is not a valid entry type.");
        }
    }

    private static void EnsurePositiveAmount(Money amount)
    {
        if (!amount.IsPositive)
        {
            throw new DomainException(
                "entry.amount_not_positive",
                "The amount must be greater than zero; use the entry type to express a debit.");
        }
    }

    private static void EnsureUsableEntryDate(DateOnly entryDate, IClock clock)
    {
        var today = clock.Today;

        if (entryDate > today)
        {
            throw new DomainException(
                "entry.date_in_future",
                $"The entry date {entryDate:yyyy-MM-dd} is in the future.");
        }

        if (entryDate < today.AddDays(-MaxBackdatingDays))
        {
            throw new DomainException(
                "entry.date_too_old",
                $"The entry date {entryDate:yyyy-MM-dd} is more than {MaxBackdatingDays} days old.");
        }
    }

    private static string NormalizeDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("entry.description_required", "A description is required.");
        }

        var trimmed = description.Trim();

        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new DomainException(
                "entry.description_too_long",
                $"The description must not exceed {MaxDescriptionLength} characters.");
        }

        return trimmed;
    }

    private static string? NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var trimmed = category.Trim();

        if (trimmed.Length > MaxCategoryLength)
        {
            throw new DomainException(
                "entry.category_too_long",
                $"The category must not exceed {MaxCategoryLength} characters.");
        }

        return trimmed;
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var trimmed = idempotencyKey.Trim();

        if (trimmed.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException(
                "entry.idempotency_key_too_long",
                $"The idempotency key must not exceed {MaxIdempotencyKeyLength} characters.");
        }

        return trimmed;
    }
}
