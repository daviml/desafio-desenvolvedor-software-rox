using CashFlow.Launches.Domain.Entries;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Results;

namespace CashFlow.Launches.Application.Entries.GetEntryById;

public sealed record GetEntryByIdQuery(Guid EntryId) : IQuery<EntryResponse>;

public sealed class GetEntryByIdQueryHandler(IEntryRepository entries)
    : IRequestHandler<GetEntryByIdQuery, EntryResponse>
{
    public async Task<Result<EntryResponse>> HandleAsync(
        GetEntryByIdQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await entries.GetByIdAsync(new EntryId(request.EntryId), cancellationToken);

        return entry is null
            ? EntryErrors.NotFound(request.EntryId)
            : EntryResponse.FromEntry(entry);
    }
}
