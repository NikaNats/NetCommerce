using StackExchange.Redis;

namespace NetCommerce.SharedKernel.Infrastructure.Redis;

/// <summary>
///     Redis-based idempotency service for preventing duplicate operations.
/// </summary>
public sealed class RedisIdempotencyService : IIdempotencyService
{
    private const string KeyPrefix = "idempotency:";
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);
    private readonly IDatabase _database;

    public RedisIdempotencyService(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await _database.KeyExistsAsync(GetKey(idempotencyKey));
    }

    public async Task SetAsync(
        string idempotencyKey,
        string result,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        await _database.StringSetAsync(
            GetKey(idempotencyKey),
            result,
            expiry ?? DefaultExpiry);
    }

    public async Task<string?> GetAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await _database.StringGetAsync(GetKey(idempotencyKey));
    }

    private static string GetKey(string idempotencyKey)
    {
        return $"{KeyPrefix}{idempotencyKey}";
    }
}