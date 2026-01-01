namespace NetCommerce.SharedKernel.Infrastructure;

/// <summary>
///     Distributed lock service interface for concurrency control.
///     Implemented using Redis Redlock algorithm.
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    ///     Acquires a distributed lock with the specified resource key.
    /// </summary>
    /// <param name="resource">The resource identifier to lock.</param>
    /// <param name="expiryTime">Lock expiration time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lock handle if acquired, null otherwise.</returns>
    Task<IDistributedLock?> AcquireLockAsync(
        string resource,
        TimeSpan expiryTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Tries to acquire a lock, waiting up to the specified time.
    /// </summary>
    Task<IDistributedLock?> TryAcquireLockAsync(
        string resource,
        TimeSpan expiryTime,
        TimeSpan waitTime,
        TimeSpan retryTime,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Represents an acquired distributed lock.
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    string Resource { get; }
    bool IsAcquired { get; }
    Task ReleaseAsync();
}