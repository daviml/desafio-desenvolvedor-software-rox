namespace CashFlow.Messaging.Contracts;

/// <summary>Nature of a cash flow entry as published on the wire.</summary>
public enum EntryType
{
    /// <summary>Money in.</summary>
    Credit = 1,

    /// <summary>Money out.</summary>
    Debit = 2,
}
