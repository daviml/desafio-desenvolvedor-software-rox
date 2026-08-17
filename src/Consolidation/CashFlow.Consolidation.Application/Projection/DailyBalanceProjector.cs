using CashFlow.Consolidation.Application.Abstractions;
using CashFlow.Consolidation.Domain.DailyBalances;
using CashFlow.Messaging.Contracts;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidation.Application.Projection;

/// <summary>
/// The one place where an entry event becomes a change to the consolidated daily balance.
/// Both event handlers delegate here, so the "apply once, atomically" rule exists only once.
/// </summary>
/// <remarks>
/// A lost write race surfaces as <see cref="ConcurrencyConflictException"/> and is deliberately
/// <em>not</em> handled here: retrying needs a clean unit of work, which only the caller can
/// provide. <see cref="RetryingDailyBalanceProjection"/> is that caller in production.
/// </remarks>
public sealed class DailyBalanceProjector(
    IDailyBalanceRepository dailyBalances,
    IProcessedEventStore processedEvents,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<DailyBalanceProjector> logger) : IDailyBalanceProjection
{
    public Task ApplyAsync(EntryRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return ProjectAsync(
            integrationEvent.EventId,
            EntryRegisteredIntegrationEvent.WireName,
            new MerchantId(integrationEvent.MerchantId),
            integrationEvent.EntryDate,
            integrationEvent.Currency,
            (balance, amount, now) =>
            {
                if (integrationEvent.Type == EntryType.Credit)
                {
                    balance.ApplyCredit(amount, now);
                }
                else
                {
                    balance.ApplyDebit(amount, now);
                }
            },
            integrationEvent.Amount,
            createIfMissing: true,
            cancellationToken);
    }

    public Task ApplyAsync(EntryCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return ProjectAsync(
            integrationEvent.EventId,
            EntryCancelledIntegrationEvent.WireName,
            new MerchantId(integrationEvent.MerchantId),
            integrationEvent.EntryDate,
            integrationEvent.Currency,
            (balance, amount, now) =>
            {
                if (integrationEvent.Type == EntryType.Credit)
                {
                    balance.ReverseCredit(amount, now);
                }
                else
                {
                    balance.ReverseDebit(amount, now);
                }
            },
            integrationEvent.Amount,
            createIfMissing: false,
            cancellationToken);
    }

    private async Task ProjectAsync(
        Guid eventId,
        string eventType,
        MerchantId merchantId,
        DateOnly date,
        string currency,
        Action<DailyBalance, Money, DateTimeOffset> mutate,
        decimal amount,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        if (await processedEvents.HasProcessedAsync(eventId, cancellationToken))
        {
            logger.LogDebug("Event {EventId} was already applied; skipping", eventId);
            return;
        }

        var balance = await dailyBalances.FindAsync(merchantId, date, cancellationToken);

        if (balance is null)
        {
            if (!createIfMissing)
            {
                // A cancellation for a day we never consolidated means the registration event has
                // not been applied yet. Throwing makes the broker redeliver it after the retry
                // backoff, by which time the ordering will have resolved itself.
                throw new InvalidOperationException(
                    $"No consolidated balance exists for merchant {merchantId} on {date:yyyy-MM-dd}; " +
                    "the corresponding registration has not been processed yet.");
            }

            balance = DailyBalance.Open(merchantId, date, currency);
            dailyBalances.Add(balance);
        }

        var now = clock.UtcNow;
        mutate(balance, Money.From(amount, currency), now);

        processedEvents.MarkProcessed(eventId, eventType, now);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateProcessedEventException)
        {
            // A concurrent consumer applied the very same event first. Nothing left to do.
            logger.LogDebug("Event {EventId} was applied concurrently; treating as a duplicate", eventId);
            return;
        }

        logger.LogInformation(
            "Applied {EventType} {EventId} to the balance of merchant {MerchantId} on {Date}",
            eventType,
            eventId,
            merchantId.Value,
            date);
    }
}
