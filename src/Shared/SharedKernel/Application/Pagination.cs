namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     Pagination parameters for list queries.
/// </summary>
public record PagedRequest(int PageNumber = 1, int PageSize = 20)
{
    public int Skip => (PageNumber - 1) * PageSize;
}

/// <summary>
///     Paginated result container.
/// </summary>
public record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
}