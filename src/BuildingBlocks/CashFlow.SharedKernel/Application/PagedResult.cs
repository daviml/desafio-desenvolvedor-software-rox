namespace CashFlow.SharedKernel.Application;

/// <summary>
/// A page of results plus the metadata a client needs to keep paging.
/// Reports are always paged so a merchant with years of history cannot exhaust server memory.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;
}
