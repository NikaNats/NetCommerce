using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Tests.SharedKernel;

namespace NetCommerce.Catalog.Infrastructure;

public class TestCatalogDbContext : DbContext
{
    public TestCatalogDbContext(DbContextOptions options) : base(options) { }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResilientTransactionTestEntity>();
    }
}
