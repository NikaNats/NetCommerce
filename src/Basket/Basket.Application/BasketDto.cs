#nullable enable
namespace NetCommerce.Basket.Application;

/// <summary>
///     Basket as seen across the remote boundary (PEAA Remote Facade + DTO).
/// </summary>
/// <remarks>
/// <para>
///     PEAA: DTOs are transport structures, not domain models. <see cref="ShoppingBasket"/>
///     is the persistence/domain representation stored in Redis; this DTO is the
///     coarse, version-aware contract returned to storefront clients. The shapes are
///     intentionally identical today so the wire format does not change, but they
///     must evolve independently: domain behavior and storage layout must never leak
///     into — or be constrained by — the client contract.
/// </para>
/// </remarks>
public sealed record BasketDto(
    string CustomerId,
    IReadOnlyList<BasketItemDto> Items,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    decimal TotalPrice);

/// <summary>
///     Single basket line across the remote boundary.
/// </summary>
public sealed record BasketItemDto(
    Guid ProductId,
    string ProductName,
    string? Sku,
    decimal Price,
    int Quantity,
    string? ImageUrl);

/// <summary>
///     Translates the domain/persistence model into the transport contract.
///     Lives in Application so both the Service Layer and presentation share one
///     mapping rule instead of scattering projections across endpoints.
/// </summary>
public static class BasketMapper
{
    public static BasketDto ToDto(ShoppingBasket basket)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var items = basket.Items
            .Select(i => new BasketItemDto(
                i.ProductId,
                i.ProductName,
                i.Sku,
                i.Price,
                i.Quantity,
                i.ImageUrl))
            .ToList();

        return new BasketDto(
            basket.CustomerId,
            items,
            basket.CreatedAt,
            basket.LastUpdatedAt,
            basket.TotalPrice);
    }
}
