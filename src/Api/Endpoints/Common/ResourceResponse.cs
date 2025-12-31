namespace NetCommerce.Api.Endpoints.Common;

/// <summary>
/// Resource response wrapper that includes HATEOAS links.
/// Follows RESTful best practices by providing navigation information.
/// </summary>
/// <typeparam name="T">The type of the resource.</typeparam>
public sealed record ResourceResponse<T> where T : class
{
    /// <summary>
    /// The resource data.
    /// </summary>
    public required T Data { get; init; }

    /// <summary>
    /// HATEOAS links for the resource.
    /// </summary>
    public required IReadOnlyList<Link> Links { get; init; }

    /// <summary>
    /// Creates a resource response with standard CRUD links.
    /// </summary>
    public static ResourceResponse<T> Create(T data, string selfUrl, params Link[] additionalLinks)
    {
        var links = new List<Link>
        {
            new("self", selfUrl, "GET"),
            new("update", selfUrl, "PUT"),
            new("delete", selfUrl, "DELETE")
        };

        links.AddRange(additionalLinks);

        return new ResourceResponse<T>
        {
            Data = data,
            Links = links
        };
    }

    /// <summary>
    /// Creates a read-only resource response (no update/delete links).
    /// </summary>
    public static ResourceResponse<T> CreateReadOnly(T data, string selfUrl, params Link[] additionalLinks)
    {
        var links = new List<Link>
        {
            new("self", selfUrl, "GET")
        };

        links.AddRange(additionalLinks);

        return new ResourceResponse<T>
        {
            Data = data,
            Links = links
        };
    }
}

/// <summary>
/// Collection response with HATEOAS links for the collection itself.
/// Use this for non-paginated collections.
/// </summary>
/// <typeparam name="T">The type of items in the collection.</typeparam>
public sealed record CollectionResponse<T>
{
    /// <summary>
    /// The collection of items.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Total number of items in the collection.
    /// </summary>
    public required int Count { get; init; }

    /// <summary>
    /// HATEOAS links for the collection.
    /// </summary>
    public required IReadOnlyList<Link> Links { get; init; }

    /// <summary>
    /// Creates a collection response with a self link.
    /// </summary>
    public static CollectionResponse<T> Create(IReadOnlyList<T> items, string selfUrl, params Link[] additionalLinks)
    {
        var links = new List<Link>
        {
            new("self", selfUrl, "GET"),
            new("create", selfUrl, "POST")
        };

        links.AddRange(additionalLinks);

        return new CollectionResponse<T>
        {
            Items = items,
            Count = items.Count,
            Links = links
        };
    }
}
