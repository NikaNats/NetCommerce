using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Catalog.Infrastructure.Persistence;

/// <summary>
/// Catalog module DbContext - uses 'catalog' schema for logical separation.
/// </summary>
public sealed class CatalogDbContext : BaseDbContext
{
    public const string Schema = "catalog";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options, IMediator mediator) 
        : base(options, mediator)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
