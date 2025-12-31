using NetCommerce.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Inventory.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stock repository implementation.
/// Uses AsNoTracking for read-only queries to improve performance.
/// </summary>
public class StockRepository : BaseRepository<Stock, Guid>, IStockRepository
{
    private readonly InventoryDbContext _context;

    public StockRepository(InventoryDbContext dbContext) : base(dbContext)
    {
        _context = dbContext;
    }

    public async Task<Stock?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Reservations.Where(r => r.Status == ReservationStatus.Active))
            .FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);
    }

    public async Task<Stock?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Reservations.Where(r => r.Status == ReservationStatus.Active))
            .FirstOrDefaultAsync(s => s.Sku == sku, cancellationToken);
    }

    public async Task<IReadOnlyList<Stock>> GetLowStockItemsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .Include(s => s.Reservations.Where(r => r.Status == ReservationStatus.Active))
            .Where(s => s.Quantity <= s.LowStockThreshold)
            .ToListAsync(cancellationToken);
    }

    public async Task<StockReservation?> GetReservationByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _context.StockReservations
            .FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
    }

    public override async Task<Stock?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Reservations.Where(r => r.Status == ReservationStatus.Active))
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }
}

