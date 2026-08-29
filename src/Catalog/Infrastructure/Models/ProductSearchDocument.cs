namespace NetCommerce.Catalog.Infrastructure.Models;

/// <summary>
///     Meilisearch read model for product search.
///     Optimized for &lt;50ms search latency with typo tolerance, faceting, and highlighting.
/// </summary>
public sealed record ProductSearchDocument(
    string Id,
    string Sku,
    string Slug,
    string Name,
    string? Description,
    decimal Price,
    string[] Categories,
    string[] Tags,
    bool IsPublished,
    int StockQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);
