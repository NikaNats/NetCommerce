#nullable enable

using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.EfCore.Persistence;
using NetCommerce.Shipping.Application.Repositories;
using NetCommerce.Shipping.Domain;

namespace NetCommerce.Shipping.Infrastructure.Persistence.Repositories;

/// <summary>
///     EF Core implementation of IShipmentRepository.
/// </summary>
public sealed class ShipmentRepository : BaseRepository<Shipment, Guid>, IShipmentRepository
{
    private readonly ShippingDbContext _context;

    public ShipmentRepository(ShippingDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Shipment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _context.Shipments
            .FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
    }

    public async Task<Shipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        return await _context.Shipments
            .FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<Shipment>> GetInTransitByCourierAsync(
        string courierProvider,
        CancellationToken cancellationToken = default)
    {
        return await _context.Shipments
            .Where(s => s.CourierProvider == courierProvider && s.Status == ShipmentStatus.InTransit)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Shipment>> GetShipmentsNeedingTrackingUpdateAsync(
        TimeSpan minAge,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - minAge;

        return await _context.Shipments
            .Where(s => s.Status == ShipmentStatus.InTransit)
            .Where(s => s.CreatedAt < cutoff)
            .OrderBy(s => s.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }
}
