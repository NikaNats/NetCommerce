using MediatR;
using NetCommerce.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Inventory.Infrastructure.Persistence;

public class InventoryDbContext : BaseDbContext
{
    public const string Schema = "inventory";
    
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options, IMediator mediator) 
        : base(options, mediator)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }
}

