using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace NetCommerce.SharedKernel.Infrastructure.Redis;

/// <summary>
///     Distributed lock service using Redis Redlock algorithm.
///     Provides concurrency control for operations like inventory reservation.
/// </summary>
public sealed class RedisDistributedLockService : IDistributedLockService, IAsyncDisposable
{
    private readonly RedLockFactory _lockFactory;

    public RedisDistributedLockService(IConnectionMultiplexer connectionMultiplexer)
    {
        var multiplexers = new List<RedLockMultiplexer>
        {
            new(connectionMultiplexer)
        };
        _lockFactory = RedLockFactory.Create(multiplexers);
    }

    public ValueTask DisposeAsync()
    {
        _lockFactory.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task<IDistributedLock?> AcquireLockAsync(
        string resource,
        TimeSpan expiryTime,
        CancellationToken cancellationToken = default)
    {
        var redLock = await _lockFactory.CreateLockAsync(
            resource,
            expiryTime);

        return redLock.IsAcquired
            ? new RedisDistributedLock(redLock)
            : null;
    }

    public async Task<IDistributedLock?> TryAcquireLockAsync(
        string resource,
        TimeSpan expiryTime,
        TimeSpan waitTime,
        TimeSpan retryTime,
        CancellationToken cancellationToken = default)
    {
        var redLock = await _lockFactory.CreateLockAsync(
            resource,
            expiryTime,
            waitTime,
            retryTime,
            cancellationToken);

        return redLock.IsAcquired
            ? new RedisDistributedLock(redLock)
            : null;
    }

    private sealed class RedisDistributedLock : IDistributedLock
    {
        private readonly IRedLock _redLock;

        public RedisDistributedLock(IRedLock redLock)
        {
            _redLock = redLock;
        }

        public string Resource => _redLock.Resource;
        public bool IsAcquired => _redLock.IsAcquired;

        public Task ReleaseAsync()
        {
            _redLock.Dispose();
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
        }
    }
}