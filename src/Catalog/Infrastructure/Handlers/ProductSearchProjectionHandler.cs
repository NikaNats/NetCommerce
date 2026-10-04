#region

using Meilisearch;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Models;
using NetCommerce.Catalog.Infrastructure.Services;
using NetCommerce.Domain.Shared;
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
    /// <remarks>
    /// <para>
    ///     <c>[NonTransactional]</c> is REQUIRED, not an optimisation.
    /// </para>
    /// <para>
    ///     Wolverine's auto-transaction middleware inspects a handler's parameters to
    ///     decide which DbContext owns the transaction. This handler takes
    ///     <c>IProductRepository</c> (Catalog) AND <c>IStockQueryService</c> (Inventory),
    ///     so two DbContexts match and Wolverine refuses to guess:
    /// </para>
    ///     <code>
    ///     Cannot determine the DbContext type for Message Handler for
    ///     ProductPublishedDomainEvent, multiple DbContext types detected:
    ///     CatalogDbContext, InventoryDbContext
    ///     </code>
    /// <para>
    ///     That is a startup failure, and it surfaces only when the host actually
    ///     boots — so it hides until an integration test starts the API. A transaction
    ///     would be wrong here regardless: the work is a cache eviction and a search
    ///     projection to Meilisearch, neither of which is a database write that needs
    ///     an ambient transaction. The event is already delivered through Wolverine's
    ///     outbox AFTER the originating transaction commits.
    ///     </para>
    /// </remarks>
    [Wolverine.Attributes.NonTransactional]
    public static async Task Handle(
        ProductPublishedDomainEvent @event,
        IProductRepository productRepository,
        IStockQueryService stockQueryService,
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

            // Fetch stock quantity from Inventory module
            var stockQuantity = await stockQueryService.GetAvailableQuantityAsync(
                product.Id,
                cancellationToken);

            // Document shape is owned by ProductSearchDocument.FromProduct so the
            // live projection and the full rebuild cannot drift apart.
            var searchDocument = ProductSearchDocument.FromProduct(product, stockQuantity);

            // Get or create Meilisearch index
            Index? index = meilisearchClient.Index(ProductsIndexName);

            // Index settings are owned by SearchIndexBootstrapper (once per
            // process with retry cooldown).
            await SearchIndexBootstrapper.EnsureConfiguredAsync(index, logger, cancellationToken);

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
}
