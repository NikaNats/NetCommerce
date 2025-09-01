using Catalog.Domain.Aggregates;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Repositories;

/// <summary>
/// Repository contract for Product aggregate operations.
/// This interface follows the Repository pattern and defines the domain's expectations
/// for data access without coupling to specific persistence technologies.
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Retrieves a product by its identifier.
    /// </summary>
    /// <param name="id">The product identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The product if found, null otherwise</returns>
    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves products by their product type.
    /// </summary>
    /// <param name="productTypeId">The product type identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of products of the specified type</returns>
    Task<List<Product>> GetByProductTypeAsync(ProductTypeId productTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves products by their brand.
    /// </summary>
    /// <param name="brandId">The brand identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of products from the specified brand</returns>
    Task<List<Product>> GetByBrandAsync(BrandId brandId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new product to the repository.
    /// </summary>
    /// <param name="product">The product to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing product in the repository.
    /// </summary>
    /// <param name="product">The product to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a product from the repository.
    /// </summary>
    /// <param name="product">The product to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a product exists by name within a product type.
    /// Used to enforce uniqueness constraints.
    /// </summary>
    /// <param name="name">The product name</param>
    /// <param name="productTypeId">The product type identifier</param>
    /// <param name="excludeProductId">Product ID to exclude from the check (for updates)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if a product with the same name exists in the same product type</returns>
    Task<bool> ExistsWithNameAsync(string name, ProductTypeId productTypeId, ProductId? excludeProductId = null, CancellationToken cancellationToken = default);
}