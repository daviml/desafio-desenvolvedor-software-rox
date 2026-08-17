namespace CashFlow.Launches.Domain.Entries;

/// <summary>
/// Lifecycle of an entry. Financial records are never deleted: cancelling keeps the audit trail
/// and lets the consolidation service compensate the daily balance.
/// </summary>
public enum EntryStatus
{
    Active = 1,
    Cancelled = 2,
}
