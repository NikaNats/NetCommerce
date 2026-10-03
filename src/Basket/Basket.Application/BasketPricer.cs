namespace NetCommerce.Basket.Application;

/// <summary>
///     Builds basket lines from CATALOG pricing.
/// </summary>
/// <remarks>
/// <para>
///     The API endpoint used to map <c>Price = request.UnitPrice</c> from the request
///     body onto the basket item, which let a caller choose their own price — a
///     product could be added at £0.01 and the Redis-persisted total, later read at
///     checkout, would reflect it.
/// </para>
/// <para>
///     The rule lives here, in the domain, rather than only at the endpoint. A
///     future endpoint cannot reintroduce the hole by forgetting to validate a
///     field, because there is deliberately no price parameter to supply wrongly.
/// </para>
/// </remarks>
public static class BasketPricer
{
    /// <summary>
    ///     Raised when a product cannot be sold at all: unknown, or not published.
    /// </summary>
    /// <remarks>
    ///     A distinct type so the endpoint can map it to 404 without leaking whether
    ///     an unpublished product exists.
    /// </remarks>
    public sealed class ProductNotSellableException(Guid productId)
        : Exception($"Product {productId} does not exist or is not published.")
    {
        public Guid ProductId { get; } = productId;
    }

    /// <summary>
    ///     Resolves a product and returns a basket line priced from the catalog.
    /// </summary>
    /// <exception cref="ProductNotSellableException">
    ///     The product is absent or unpublished — in which case there is no
    ///     authoritative price and nothing may be added to the basket.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Quantity is not positive.</exception>
    public static async Task<BasketItem> CreateLineAsync(
        IProductPriceSource products,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(products);

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "Quantity must be positive.");

        var pricing = await products.GetPricingAsync(productId, cancellationToken)
            ?? throw new ProductNotSellableException(productId);

        return new BasketItem
        {
            ProductId = pricing.ProductId,
            ProductName = pricing.Name,
            Sku = pricing.Sku,
            ImageUrl = pricing.ImageUrl,

            // The catalog is the authority. Nothing from the request body reaches
            // this field.
            Price = pricing.Price.Amount,
            Quantity = quantity
        };
    }
}