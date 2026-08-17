using CashFlow.Launches.Domain.Entries;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;

namespace CashFlow.Launches.Application.Entries.RegisterEntry;

/// <summary>Registers a credit or debit in the merchant's cash flow.</summary>
/// <param name="IdempotencyKey">
/// Optional. When supplied, replaying the same request returns the entry created the first time
/// instead of duplicating it - which is what makes client retries safe during a traffic peak.
/// </param>
public sealed record RegisterEntryCommand(
    Guid MerchantId,
    EntryType Type,
    decimal Amount,
    DateOnly EntryDate,
    string Description,
    string Currency = Money.DefaultCurrency,
    string? Category = null,
    string? IdempotencyKey = null) : ICommand<EntryResponse>;
