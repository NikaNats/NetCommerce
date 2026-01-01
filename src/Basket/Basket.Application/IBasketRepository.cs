namespace NetCommerce.Basket.Application;

/// <summary>
///     Basket repository interface for Redis storage.
/// </summary>
public interface IBasketRepository
{
    Task<ShoppingBasket?> GetBasketAsync(string customerId, CancellationToken cancellationToken = default);
    Task<ShoppingBasket> UpdateBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default);
    Task<bool> DeleteBasketAsync(string customerId, CancellationToken cancellationToken = default);
}