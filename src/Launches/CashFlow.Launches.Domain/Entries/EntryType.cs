namespace CashFlow.Launches.Domain.Entries;

/// <summary>Nature of an entry. The amount is always stored positive; this carries the sign.</summary>
public enum EntryType
{
    Credit = 1,
    Debit = 2,
}
