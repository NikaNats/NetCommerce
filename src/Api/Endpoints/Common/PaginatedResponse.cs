namespace NetCommerce.Api.Endpoints.Common;

/// <summary>
///     Standard paginated response with pagination metadata.
/// </summary>
/// <typeparam name="T">The type of items in the collection.</typeparam>
public sealed record PaginatedResponse<T>
{
    /// <summary>
    ///     The collection of items for the current page.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    ///     Pagination metadata.
    /// </summary>
    public required PaginationMetadata Pagination { get; init; }

    /// <summary>
    ///     Creates a paginated response.
    /// </summary>
    public static PaginatedResponse<T> Create(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount)
    {
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PaginatedResponse<T>
        {
            Items = items,
            Pagination = new PaginationMetadata
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPreviousPage = page > 1,
                HasNextPage = page < totalPages
            }
        };
    }
}

/// <summary>
///     Pagination metadata for paginated responses.
/// </summary>
public sealed record PaginationMetadata
{
    /// <summary>
    ///     Current page number (1-based).
    /// </summary>
    public required int Page { get; init; }

    /// <summary>
    ///     Number of items per page.
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    ///     Total number of items across all pages.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    ///     Total number of pages.
    /// </summary>
    public required int TotalPages { get; init; }

    /// <summary>
    ///     Indicates whether there is a previous page.
    /// </summary>
    public required bool HasPreviousPage { get; init; }

    /// <summary>
    ///     Indicates whether there is a next page.
    /// </summary>
    public required bool HasNextPage { get; init; }
}
