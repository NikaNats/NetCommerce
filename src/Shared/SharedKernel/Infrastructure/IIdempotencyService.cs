namespace NetCommerce.SharedKernel.Infrastructure;

/// <summary>
///     Idempotency service for preventing duplicate operations.
///     Critical for payment and order operations.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    ///     Checks if an operation with the given key has already been processed.
    /// </summary>
    Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Records that an operation has been processed.
    /// </summary>
    Task SetAsync(string idempotencyKey, string result, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the result of a previously processed operation.
    /// </summary>
    Task<string?> GetAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}