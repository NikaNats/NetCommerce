#region

using Meilisearch;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Models;
using Index = Meilisearch.Index;

#endregion

namespace NetCommerce.Catalog.Infrastructure.Handlers;

/// <summary>
///     Wolverine handlers for product search projection to Meilisearch.
///     Ensures eventual consistency between PostgreSQL (write model) and Meilisearch (read model).
///     Uses Wolverine outbox pattern for guaranteed delivery.
/// </summary>
public static class ProductSearchProjectionHandler
{
    private const string ProductsIndexName = "products";

    /// <summary>
    ///     Handles ProductPublished event by projecting product to Meilisearch search index.
    ///     Wolverine automatically handles this through the outbox pattern for guaranteed delivery.
    /// </summary>
    public static async Task Handle(
        ProductPublishedDomainEvent @event,
        IProductRepository productRepository,
        MeilisearchClient meilisearchClient,
        ILogger<ProductPublishedDomainEvent> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Fetch full product data from write model (PostgreSQL)
            Product? product = await productRepository.GetByIdAsync(@event.ProductId, cancellationToken);
            if (product is null)
            {
                logger.LogWarning("Product {ProductId} not found for search projection", @event.ProductId);
                return;
            }

            // Transform domain model to search document (read model)
            var searchDocument = new ProductSearchDocument(
                product.Id.ToString(),
                product.Sku,
                product.Slug ?? string.Empty,
                product.Name,
                product.Description,
                product.Price.Amount,
                [product.CategoryId.ToString()],
                product.Attributes.Select(a => $"{a.Key}:{a.Value}").ToArray(),
                product.Status == ProductStatus.Published,
                0, // TODO: Fetch from Inventory module via query
                DateTimeOffset.UtcNow, // Snapshot time
                null
            );

            // Get or create Meilisearch index
            Index? index = meilisearchClient.Index(ProductsIndexName);

            // Configure searchable attributes and ranking rules on first use
            await ConfigureIndexIfNeeded(index, logger, cancellationToken);

            // Add/update document in search index
            await index.AddDocumentsAsync([searchDocument], "Id", cancellationToken);

            logger.LogInformation(
                "Product {ProductId} ({Sku}) projected to Meilisearch search index",
                product.Id,
                product.Sku);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to project product {ProductId} to Meilisearch search index. Wolverine will retry.",
                @event.ProductId);
            throw; // Wolverine will retry based on error policy
        }
    }

    /// <summary>
    ///     Handles ProductPriceChanged event by updating price in Meilisearch index.
    ///     Only updates the Price field to avoid unnecessary data transfer.
    /// </summary>
    public static async Task Handle(
        ProductPriceChangedDomainEvent @event,
        MeilisearchClient meilisearchClient,
        ILogger<ProductPriceChangedDomainEvent> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            Index? index = meilisearchClient.Index(ProductsIndexName);

            // Partial update: only update the Price field
            var update = new Dictionary<string, object>
            {
                ["Id"] = @event.ProductId.ToString(),
                ["Price"] = @event.NewPrice.Amount,
                ["UpdatedAt"] = DateTimeOffset.UtcNow
            };

            await index.UpdateDocumentsAsync([update], "Id", cancellationToken);

            logger.LogInformation(
                "Product {ProductId} price updated in search index: {OldPrice} -> {NewPrice}",
                @event.ProductId,
                @event.OldPrice.Amount,
                @event.NewPrice.Amount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to update product {ProductId} price in Meilisearch. Wolverine will retry.",
                @event.ProductId);
            throw;
        }
    }

    /// <summary>
    ///     Handles ProductArchived event by removing product from Meilisearch index.
    ///     Archived products should not appear in search results.
    /// </summary>
    public static async Task Handle(
        ProductArchivedDomainEvent @event,
        MeilisearchClient meilisearchClient,
        ILogger<ProductArchivedDomainEvent> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            Index? index = meilisearchClient.Index(ProductsIndexName);

            // Remove document from search index
            await index.DeleteOneDocumentAsync(@event.ProductId.ToString(), cancellationToken);

            logger.LogInformation(
                "Product {ProductId} removed from search index (archived)",
                @event.ProductId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to remove product {ProductId} from Meilisearch. Wolverine will retry.",
                @event.ProductId);
            throw;
        }
    }

    private static async Task ConfigureIndexIfNeeded(
        Index index,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Configure searchable attributes for typo tolerance and relevance
            await index.UpdateSearchableAttributesAsync(
                ["Name", "Description", "Sku", "Tags"],
                cancellationToken);

            // Configure filterable attributes for faceting
            await index.UpdateFilterableAttributesAsync(
                ["Categories", "Price", "IsPublished", "StockQuantity"],
                cancellationToken);

            // Configure ranking rules for search relevance
            await index.UpdateRankingRulesAsync(
                [
                    "words", // Typo tolerance
                    "typo", // Number of typos
                    "proximity", // Word proximity
                    "attribute", // Attribute order (Name > Description > Tags)
                    "sort", // Custom sort
                    "exactness" // Exact matches
                ],
                cancellationToken);

            logger.LogInformation("Meilisearch index '{IndexName}' configured successfully", ProductsIndexName);
        }
        catch (Exception ex)
        {
            // Index configuration is idempotent, safe to ignore if already configured
            logger.LogDebug(ex, "Meilisearch index configuration skipped (likely already configured)");
        }
    }
}
