using NetCommerce.Catalog.Domain.Products;

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
)
{
    /// <summary>
    ///     The single owner of the domain-to-document shape.
    ///     Both the event-driven projection (ProductSearchProjectionHandler) and the
    ///     full rebuild (SearchIndexRebuildService) build documents here, so the
    ///     indexed shape cannot drift between the live path and the recovery path.
    /// </summary>
    public static ProductSearchDocument FromProduct(
        Product product,
        int stockQuantity)
    {
        return new ProductSearchDocument(
            product.Id.ToString(),
            product.Sku,
            product.Slug ?? string.Empty,
            product.Name,
            product.Description,
            product.Price.Amount,
            [product.CategoryId.ToString()],
            product.Attributes.Select(a => $"{a.Key}:{a.Value}").ToArray(),
            product.Status == ProductStatus.Published,
            stockQuantity,
            DateTimeOffset.UtcNow,
            null);
    }
}
