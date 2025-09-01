using Catalog.Domain.Aggregates;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Repositories;

/// <summary>
/// Repository contract for ProductType aggregate operations.
/// </summary>
public interface IProductTypeRepository
{
    /// <summary>
    /// Retrieves a product type by its identifier.
    /// </summary>
    /// <param name="id">The product type identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The product type if found, null otherwise</returns>
    Task<ProductType?> GetByIdAsync(ProductTypeId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a product type by its name.
    /// </summary>
    /// <param name="name">The product type name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The product type if found, null otherwise</returns>
    Task<ProductType?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all product types.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of all product types</returns>
    Task<List<ProductType>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new product type to the repository.
    /// </summary>
    /// <param name="productType">The product type to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(ProductType productType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing product type in the repository.
    /// </summary>
    /// <param name="productType">The product type to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAsync(ProductType productType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a product type from the repository.
    /// </summary>
    /// <param name="productType">The product type to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(ProductType productType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a product type name is already in use.
    /// Used to enforce name uniqueness.
    /// </summary>
    /// <param name="name">The product type name</param>
    /// <param name="excludeProductTypeId">Product type ID to exclude from the check (for updates)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the name is already in use</returns>
    Task<bool> IsNameInUseAsync(string name, ProductTypeId? excludeProductTypeId = null, CancellationToken cancellationToken = default);
}