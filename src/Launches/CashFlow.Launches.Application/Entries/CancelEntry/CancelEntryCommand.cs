using CashFlow.SharedKernel.Application;

namespace CashFlow.Launches.Application.Entries.CancelEntry;

/// <summary>Cancels an entry, compensating the consolidated balance instead of deleting history.</summary>
public sealed record CancelEntryCommand(Guid EntryId, string? Reason = null) : ICommand<EntryResponse>;
