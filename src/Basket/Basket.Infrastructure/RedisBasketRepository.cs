using System.Text.Json;
using System.Text.Json.Serialization;
using NetCommerce.Basket.Application;
using StackExchange.Redis;

namespace NetCommerce.Basket.Infrastructure;

/// <summary>
///     Source-generated JSON context for AOT-safe serialization.
/// </summary>
[JsonSerializable(typeof(ShoppingBasket))]
internal sealed partial class BasketJsonContext : JsonSerializerContext;

/// <summary>
///     Redis-based basket repository implementation.
/// </summary>
public sealed class RedisBasketRepository : IBasketRepository
{
    private const string KeyPrefix = "basket:";
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromDays(30);
    private readonly IDatabase _database;

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

        if (data.IsNullOrEmpty) return null;

        return JsonSerializer.Deserialize(data.ToString(), BasketJsonContext.Default.ShoppingBasket);
    }

    public async Task<ShoppingBasket> UpdateBasketAsync(
        ShoppingBasket basket,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(basket.CustomerId);
        var json = JsonSerializer.Serialize(basket, BasketJsonContext.Default.ShoppingBasket);

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

    private static string GetKey(string customerId)
    {
        return $"{KeyPrefix}{customerId}";
    }
}
