using Catalog.Domain.Aggregates;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Repositories;

/// <summary>
/// Repository contract for Variant aggregate operations.
/// </summary>
public interface IVariantRepository
{
    /// <summary>
    /// Retrieves a variant by its identifier.
    /// </summary>
    /// <param name="id">The variant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The variant if found, null otherwise</returns>
    Task<Variant?> GetByIdAsync(VariantId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a variant by its SKU.
    /// </summary>
    /// <param name="sku">The SKU to search for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The variant if found, null otherwise</returns>
    Task<Variant?> GetBySkuAsync(SKU sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all variants for a specific product.
    /// </summary>
    /// <param name="productId">The product identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of variants for the specified product</returns>
    Task<List<Variant>> GetByProductIdAsync(ProductId productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the number of active variants for a product.
    /// Used by Product aggregate to enforce business rules.
    /// </summary>
    /// <param name="productId">The product identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The count of active variants</returns>
    Task<int> CountActiveVariantsForProductAsync(ProductId productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new variant to the repository.
    /// </summary>
    /// <param name="variant">The variant to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(Variant variant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing variant in the repository.
    /// </summary>
    /// <param name="variant">The variant to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAsync(Variant variant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a variant from the repository.
    /// </summary>
    /// <param name="variant">The variant to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(Variant variant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a SKU is already in use.
    /// Used to enforce SKU uniqueness across all variants.
    /// </summary>
    /// <param name="sku">The SKU to check</param>
    /// <param name="excludeVariantId">Variant ID to exclude from the check (for updates)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the SKU is already in use</returns>
    Task<bool> IsSkuInUseAsync(SKU sku, VariantId? excludeVariantId = null, CancellationToken cancellationToken = default);
}