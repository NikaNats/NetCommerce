namespace NetCommerce.Api.Endpoints.Common;

/// <summary>
/// Standard paginated response following RESTful best practices.
/// Includes pagination metadata and HATEOAS links.
/// </summary>
/// <typeparam name="T">The type of items in the collection.</typeparam>
public sealed record PaginatedResponse<T>
{
    /// <summary>
    /// The collection of items for the current page.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Pagination metadata.
    /// </summary>
    public required PaginationMetadata Pagination { get; init; }

    /// <summary>
    /// HATEOAS links for navigation.
    /// </summary>
    public required IReadOnlyList<Link> Links { get; init; }

    /// <summary>
    /// Creates a paginated response with proper HATEOAS links.
    /// </summary>
    public static PaginatedResponse<T> Create(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount,
        string baseUrl)
    {
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var links = new List<Link>
        {
            new("self", $"{baseUrl}?page={page}&pageSize={pageSize}", "GET"),
            new("first", $"{baseUrl}?page=1&pageSize={pageSize}", "GET"),
            new("last", $"{baseUrl}?page={totalPages}&pageSize={pageSize}", "GET")
        };

        if (page > 1)
        {
            links.Add(new("prev", $"{baseUrl}?page={page - 1}&pageSize={pageSize}", "GET"));
        }

        if (page < totalPages)
        {
            links.Add(new("next", $"{baseUrl}?page={page + 1}&pageSize={pageSize}", "GET"));
        }

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
            },
            Links = links
        };
    }
}

/// <summary>
/// Pagination metadata for paginated responses.
/// </summary>
public sealed record PaginationMetadata
{
    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public required int Page { get; init; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// Total number of items across all pages.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public required int TotalPages { get; init; }

    /// <summary>
    /// Indicates whether there is a previous page.
    /// </summary>
    public required bool HasPreviousPage { get; init; }

    /// <summary>
    /// Indicates whether there is a next page.
    /// </summary>
    public required bool HasNextPage { get; init; }
}

/// <summary>
/// HATEOAS link for resource navigation.
/// </summary>
/// <param name="Rel">The relationship type (e.g., "self", "next", "prev").</param>
/// <param name="Href">The URI for the link.</param>
/// <param name="Method">The HTTP method to use.</param>
public sealed record Link(string Rel, string Href, string Method);
