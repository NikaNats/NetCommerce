namespace NetCommerce.Basket.Application;

using NetCommerce.Domain.Shared;

/// <summary>
///     The catalog's answer to "what does this product cost, and how is it
///     described?"
/// </summary>
/// <remarks>
/// <para>
///     A deliberate DEPENDENCY INVERSION rather than a direct reference to
///     <c>Catalog.Domain</c>. The Basket module must not reference Catalog: modules
///     in this monolith stay independent, and a compile-time edge from Basket to
///     Catalog would couple their release and make the boundary unenforceable.
/// </para>
/// <para>
///     The API layer implements this over <c>IProductRepository</c>. Basket
///     depends only on this contract, so it cannot learn anything about how
///     products are stored, and the catalog can change its model without touching
///     a basket test.
/// </para>
/// </remarks>
public interface IProductPriceSource
{
    /// <summary>
    ///     Resolves the sellable snapshot of a product, or null when no published
    ///     product with that id exists.
    /// </summary>
    Task<ProductPricing?> GetPricingAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <summary>
///     The catalog-authoritative facts a basket line needs.
/// </summary>
/// <param name="ProductId">Catalog identity.</param>
/// <param name="Name">Display name, from the catalog.</param>
/// <param name="Sku">Stock keeping unit, from the catalog.</param>
/// <param name="Price">Unit price, from the catalog. Never supplied by a caller.</param>
/// <param name="ImageUrl">Primary image, or null when the product has none.</param>
public readonly record struct ProductPricing(
    Guid ProductId,
    string Name,
    string? Sku,
    Money Price,
    string? ImageUrl);