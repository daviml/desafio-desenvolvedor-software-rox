using CashFlow.Launches.Domain.Entries;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Results;
using FluentValidation;

namespace CashFlow.Launches.Application.Entries.ListEntries;

/// <summary>Paged listing of a merchant's entries. Always bounded - never "select everything".</summary>
public sealed record ListEntriesQuery(
    Guid MerchantId,
    DateOnly? From = null,
    DateOnly? To = null,
    EntryType? Type = null,
    bool IncludeCancelled = true,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<EntryResponse>>;

public sealed class ListEntriesQueryValidator : AbstractValidator<ListEntriesQuery>
{
    public const int MaxPageSize = 200;

    public ListEntriesQueryValidator()
    {
        RuleFor(query => query.MerchantId).NotEmpty();
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, MaxPageSize);

        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From!.Value)
            .When(query => query is { From: not null, To: not null })
            .WithMessage("'To' must be on or after 'From'.");
    }
}

public sealed class ListEntriesQueryHandler(IEntryQueries entryQueries)
    : IRequestHandler<ListEntriesQuery, PagedResult<EntryResponse>>
{
    public async Task<Result<PagedResult<EntryResponse>>> HandleAsync(
        ListEntriesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criteria = new EntrySearchCriteria(
            new MerchantId(request.MerchantId),
            request.From,
            request.To,
            request.Type,
            request.IncludeCancelled,
            request.Page,
            request.PageSize);

        var (items, totalCount) = await entryQueries.SearchAsync(criteria, cancellationToken);

        var page = new PagedResult<EntryResponse>(
            [.. items.Select(EntryResponse.FromSummary)],
            request.Page,
            request.PageSize,
            totalCount);

        return page;
    }
}
