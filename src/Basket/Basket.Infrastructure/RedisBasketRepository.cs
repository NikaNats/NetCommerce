using System.Text.Json;
using NetCommerce.Basket.Application;
using StackExchange.Redis;

namespace NetCommerce.Basket.Infrastructure;

/// <summary>
/// Redis-based basket repository implementation.
/// </summary>
public sealed class RedisBasketRepository : IBasketRepository
{
    private readonly IDatabase _database;
    private const string KeyPrefix = "basket:";
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromDays(30);

    public RedisBasketRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<ShoppingBasket?> GetBasketAsync(
        string customerId, 
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(customerId);
        var data = await _database.StringGetAsync(key);

        if (data.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ShoppingBasket>(data.ToString());
    }

    public async Task<ShoppingBasket> UpdateBasketAsync(
        ShoppingBasket basket, 
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(basket.CustomerId);
        var json = JsonSerializer.Serialize(basket);

        await _database.StringSetAsync(key, json, DefaultExpiry);

        return basket;
    }

    public async Task<bool> DeleteBasketAsync(
        string customerId, 
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(customerId);
        return await _database.KeyDeleteAsync(key);
    }

    private static string GetKey(string customerId) => $"{KeyPrefix}{customerId}";
}

