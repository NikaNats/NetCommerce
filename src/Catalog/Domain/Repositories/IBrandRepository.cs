using Catalog.Domain.Aggregates;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Repositories;

/// <summary>
/// Repository contract for Brand aggregate operations.
/// </summary>
public interface IBrandRepository
{
    /// <summary>
    /// Retrieves a brand by its identifier.
    /// </summary>
    /// <param name="id">The brand identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The brand if found, null otherwise</returns>
    Task<Brand?> GetByIdAsync(BrandId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a brand by its name.
    /// </summary>
    /// <param name="name">The brand name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The brand if found, null otherwise</returns>
    Task<Brand?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active brands.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of active brands</returns>
    Task<List<Brand>> GetActiveBrandsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all brands.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of all brands</returns>
    Task<List<Brand>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new brand to the repository.
    /// </summary>
    /// <param name="brand">The brand to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(Brand brand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing brand in the repository.
    /// </summary>
    /// <param name="brand">The brand to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAsync(Brand brand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a brand from the repository.
    /// </summary>
    /// <param name="brand">The brand to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(Brand brand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a brand name is already in use.
    /// Used to enforce name uniqueness.
    /// </summary>
    /// <param name="name">The brand name</param>
    /// <param name="excludeBrandId">Brand ID to exclude from the check (for updates)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the name is already in use</returns>
    Task<bool> IsNameInUseAsync(string name, BrandId? excludeBrandId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a brand is referenced by any products.
    /// Used to prevent deletion of brands that are still in use.
    /// </summary>
    /// <param name="brandId">The brand identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the brand is referenced by products</returns>
    Task<bool> IsReferencedByProductsAsync(BrandId brandId, CancellationToken cancellationToken = default);
}