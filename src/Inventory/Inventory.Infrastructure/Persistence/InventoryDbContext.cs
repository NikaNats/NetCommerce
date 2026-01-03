using Microsoft.EntityFrameworkCore;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Inventory.Infrastructure.Persistence;

public class InventoryDbContext : BaseDbContext
{
    public const string Schema = "inventory";

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
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