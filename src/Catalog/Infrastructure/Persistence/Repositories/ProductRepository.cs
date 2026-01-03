#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Catalog.Infrastructure.Persistence.Repositories;

/// <summary>
///     Product repository implementation with Full-Text Search support.
///     Uses AsNoTracking for read-only queries to improve performance.
/// </summary>
public sealed class ProductRepository : BaseRepository<Product, Guid>, IProductRepository
{
    public ProductRepository(CatalogDbContext context) : base(context)
    {
    }

    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);
    }

    public async Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .Where(p => p.CategoryId == categoryId && p.Status == ProductStatus.Published)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Full-text search using PostgreSQL tsvector.
    ///     Uses AsNoTracking since search results are read-only.
    /// </summary>
    public async Task<IReadOnlyList<Product>> SearchAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return await DbSet
                .AsNoTracking()
                .Where(p => p.Status == ProductStatus.Published)
                .Take(20)
                .ToListAsync(cancellationToken);

        // PostgreSQL Full-Text Search using EF.Functions
        return await DbSet
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Published &&
                        EF.Functions.ToTsVector("english", p.Name + " " + p.Description)
                            .Matches(EF.Functions.PlainToTsQuery("english", searchTerm)))
            .OrderByDescending(p => EF.Functions.ToTsVector("english", p.Name + " " + p.Description)
                .Rank(EF.Functions.PlainToTsQuery("english", searchTerm)))
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string sku, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(p => p.Sku == sku, cancellationToken);
    }
}
