using Catalog.Domain.Aggregates;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Repositories;

/// <summary>
/// Repository contract for Category aggregate operations.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>
    /// Retrieves a category by its identifier.
    /// </summary>
    /// <param name="id">The category identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The category if found, null otherwise</returns>
    Task<Category?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all root categories (categories without parents).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of root categories</returns>
    Task<List<Category>> GetRootCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves child categories of a specific parent category.
    /// </summary>
    /// <param name="parentCategoryId">The parent category identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of child categories</returns>
    Task<List<Category>> GetChildCategoriesAsync(CategoryId parentCategoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active categories.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of active categories</returns>
    Task<List<Category>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new category to the repository.
    /// </summary>
    /// <param name="category">The category to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing category in the repository.
    /// </summary>
    /// <param name="category">The category to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a category from the repository.
    /// </summary>
    /// <param name="category">The category to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a category has any child categories.
    /// Used to prevent deletion of parent categories that still have children.
    /// </summary>
    /// <param name="categoryId">The category identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the category has child categories</returns>
    Task<bool> HasChildCategoriesAsync(CategoryId categoryId, CancellationToken cancellationToken = default);
}