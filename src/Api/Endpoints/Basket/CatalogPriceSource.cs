#nullable enable
using NetCommerce.Basket.Application;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Api.Endpoints.Basket;

/// <summary>
///     Catalog-backed implementation of <see cref="IProductPriceSource"/>.
/// </summary>
/// <remarks>
/// <para>
///     This type is the ONLY place where the Basket module learns about the catalog
///     model, and it lives in the composition root rather than in Basket. Basket
///     declares the interface; the API wires the implementation. That keeps the
///     compile-time dependency pointing inward (Api -> Catalog, Api -> Basket) with
///     no edge from Basket to Catalog, so the bounded contexts stay independent.
/// </para>
/// <para>
///     It reads through <see cref="IReadOnlyRepository{TAggregate,TId}"/> rather
///     than the catalog's own <c>IProductRepository</c>, because the endpoint needs
///     exactly one lookup by id. Depending on the narrowest available abstraction
///     also means this cannot accidentally mutate a catalog aggregate from a basket
///     request.
/// </para>
/// </remarks>
internal sealed class CatalogPriceSource(IReadOnlyRepository<Product, Guid> products)
    : IProductPriceSource
{
    public async Task<ProductPricing?> GetPricingAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(productId, cancellationToken);

        // Unpublished products (draft, archived) have no valid storefront price.
        // Returning null makes the basket reject the line, so an unpublished
        // product cannot be added at some client-invented price.
        if (product is null || product.Status != ProductStatus.Published)
            return null;

        // Images are a value list on the aggregate; prefer the primary, else the first
        // available so the basket line is not needlessly imageless.
        //
        // Indexed rather than LINQ: Images is IReadOnlyList, and CA1826 (an error in
        // Release) rejects FirstOrDefault on an indexable collection. Count is
        // checked first so the empty case cannot index.
        var images = product.Images;
        var imageKey = images.Count == 0
            ? null
            : images.FirstOrDefault(i => i.IsPrimary)?.ImageKey ?? images[0].ImageKey;

        return new ProductPricing(
            product.Id,
            product.Name,
            product.Sku,
            product.Price,
            string.IsNullOrWhiteSpace(imageKey) ? null : imageKey);
    }
}