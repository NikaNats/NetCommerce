using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.EfCore.Persistence;

namespace NetCommerce.Inventory.Infrastructure.Persistence;

[UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
    Justification = "EF Core's ApplyConfigurationsFromAssembly uses reflection by design. All entity configurations are in this assembly.")]
public class InventoryDbContext : BaseDbContext
{
    public const string Schema = "inventory";

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }
}
