using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Catalog.Domain.Products;

/// <summary>
///     Repository interface for Product aggregate.
/// </summary>
public interface IProductRepository : IRepository<Product, Guid>
{
    Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string sku, CancellationToken cancellationToken = default);
}