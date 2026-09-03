#nullable enable
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;using Microsoft.Extensions.Caching.Hybrid;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Domain.Shared;

namespace NetCommerce.Catalog.Infrastructure.Caching;

/// <summary>
///     AOT-safe <see cref="HybridCache"/> serializer for the <see cref="Product"/> aggregate.
///
///     2026 rationale: HybridCache serializes entries even for the in-process
///     stampede state, and rich aggregates intentionally expose no deserializable
///     constructor. Rather than polluting the domain model with serialization
///     concerns, this serializer translates through an immutable snapshot DTO
///     (source-generated JSON, trim-safe) and rehydrates via the domain-owned
///     <c>Product.Rehydrate</c> factory, which raises no domain events.
/// </summary>
public sealed class ProductCacheSerializer : IHybridCacheSerializer<Product?>
{
    public Product? Deserialize(ReadOnlySequence<byte> source)
    {
        if (source.IsEmpty)
            return null;

        var reader = new Utf8JsonReader(source);
        var snapshot = JsonSerializer.Deserialize(ref reader, ProductCacheJsonContext.Default.ProductCacheSnapshot);
        return snapshot is null ? null : snapshot.ToProduct();
    }

    public void Serialize(Product? value, IBufferWriter<byte> target)
    {
        if (value is null)
            return; // Empty payload round-trips to null in Deserialize.

        using var writer = new Utf8JsonWriter(target);
        JsonSerializer.Serialize(writer, ProductCacheSnapshot.FromProduct(value), ProductCacheJsonContext.Default.ProductCacheSnapshot);
    }
}

/// <summary>
///     Immutable snapshot of <see cref="Product"/> state for cache storage.
///     Primitives only, so System.Text.Json source generation stays trim-safe.
/// </summary>
public sealed record ProductCacheSnapshot(
    Guid Id,
    string Name,
    string Description,
    string Sku,
    decimal PriceAmount,
    string PriceCurrency,
    decimal WeightKg,
    Guid CategoryId,
    ProductStatus Status,
    string? SeoTitle,
    string? SeoDescription,
    string? Slug,
    IReadOnlyList<ProductImageSnapshot> Images,
    IReadOnlyList<ProductAttributeSnapshot> Attributes)
{
    public static ProductCacheSnapshot FromProduct(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Sku,
        product.Price.Amount,
        product.Price.Currency,
        product.WeightKg,
        product.CategoryId,
        product.Status,
        product.SeoTitle,
        product.SeoDescription,
        product.Slug,
        product.Images.Select(i => new ProductImageSnapshot(i.Id, i.ImageKey, i.DisplayOrder, i.IsPrimary)).ToList(),
        product.Attributes.Select(a => new ProductAttributeSnapshot(a.Key, a.Value, a.DisplayName)).ToList());

    public Product ToProduct() => Product.Rehydrate(
        Id,
        Name,
        Description,
        Sku,
        Money.Create(PriceAmount, PriceCurrency),
        WeightKg,
        CategoryId,
        Status,
        SeoTitle,
        SeoDescription,
        Slug,
        Images.Select(i => (i.Id, i.ImageKey, i.DisplayOrder, i.IsPrimary)).ToList(),
        Attributes.Select(a => (a.Key, a.Value, a.DisplayName)).ToList());
}

public sealed record ProductImageSnapshot(Guid Id, string ImageKey, int DisplayOrder, bool IsPrimary);

public sealed record ProductAttributeSnapshot(string Key, string Value, string? DisplayName);

[JsonSerializable(typeof(ProductCacheSnapshot))]
internal sealed partial class ProductCacheJsonContext : JsonSerializerContext
{
}
