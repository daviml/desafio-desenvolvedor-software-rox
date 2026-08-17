using CashFlow.Launches.Application.Abstractions;
using CashFlow.Launches.Domain.Entries;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Results;
using CashFlow.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace CashFlow.Launches.Application.Entries.RegisterEntry;

/// <summary>
/// Stores the entry and, in the same database transaction, the integration event that tells the
/// consolidation service about it (transactional outbox - see the persistence layer's interceptor).
/// The write therefore never depends on the broker being reachable.
/// </summary>
public sealed class RegisterEntryCommandHandler(
    IEntryRepository entries,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<RegisterEntryCommandHandler> logger)
    : IRequestHandler<RegisterEntryCommand, EntryResponse>
{
    public async Task<Result<EntryResponse>> HandleAsync(
        RegisterEntryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var merchantId = new MerchantId(request.MerchantId);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var replay = await entries.FindByIdempotencyKeyAsync(
                merchantId,
                request.IdempotencyKey.Trim(),
                cancellationToken);

            if (replay is not null)
            {
                logger.LogInformation(
                    "Idempotent replay for key {IdempotencyKey}; returning entry {EntryId}",
                    request.IdempotencyKey,
                    replay.Id);

                return EntryResponse.FromEntry(replay);
            }
        }

        Entry entry;
        try
        {
            entry = Entry.Register(
                merchantId,
                request.Type,
                Money.From(request.Amount, request.Currency),
                request.EntryDate,
                request.Description,
                clock,
                request.Category,
                request.IdempotencyKey);
        }
        catch (DomainException exception)
        {
            return EntryErrors.Rejected(exception.Code, exception.Message);
        }

        entries.Add(entry);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // Two concurrent retries of the same request reached the database. The unique index
            // is the source of truth, so return whichever one won instead of failing the caller.
            var winner = await entries.FindByIdempotencyKeyAsync(
                merchantId,
                request.IdempotencyKey!.Trim(),
                cancellationToken);

            return winner is not null
                ? EntryResponse.FromEntry(winner)
                : Error.Conflict("entry.duplicate_idempotency_key", "A concurrent request already used this key.");
        }

        return EntryResponse.FromEntry(entry);
    }
}
