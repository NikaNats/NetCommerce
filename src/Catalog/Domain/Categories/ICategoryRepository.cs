using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Catalog.Domain.Categories;

/// <summary>
///     Repository interface for Category aggregate.
/// </summary>
public interface ICategoryRepository : IRepository<Category, Guid>
{
    Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetRootCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetChildCategoriesAsync(Guid parentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default);
}