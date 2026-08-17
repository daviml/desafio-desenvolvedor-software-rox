using CashFlow.Launches.Domain.Entries;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Results;
using CashFlow.SharedKernel.Time;

namespace CashFlow.Launches.Application.Entries.CancelEntry;

public sealed class CancelEntryCommandHandler(
    IEntryRepository entries,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<CancelEntryCommand, EntryResponse>
{
    public async Task<Result<EntryResponse>> HandleAsync(
        CancelEntryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await entries.GetByIdAsync(new EntryId(request.EntryId), cancellationToken);

        if (entry is null)
        {
            return EntryErrors.NotFound(request.EntryId);
        }

        if (entry.IsCancelled)
        {
            return EntryErrors.AlreadyCancelled(request.EntryId);
        }

        try
        {
            entry.Cancel(request.Reason, clock);
        }
        catch (DomainException exception)
        {
            return EntryErrors.Rejected(exception.Code, exception.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return EntryResponse.FromEntry(entry);
    }
}
