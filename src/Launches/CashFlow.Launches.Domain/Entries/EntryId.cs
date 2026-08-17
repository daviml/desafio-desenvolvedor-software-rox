namespace CashFlow.Launches.Domain.Entries;

/// <summary>
/// Strongly typed identifier. Prevents a merchant id from ever being passed where an entry id
/// is expected - a class of bug that raw <see cref="Guid"/> parameters make easy to write.
/// </summary>
public readonly record struct EntryId(Guid Value)
{
    /// <summary>Version 7 GUIDs are time-ordered, which keeps clustered index inserts sequential.</summary>
    public static EntryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
