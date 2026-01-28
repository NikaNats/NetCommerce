#nullable enable

using NetCommerce.Kernel.Application;
using NetCommerce.Shipping.Domain;

namespace NetCommerce.Shipping.Application.Repositories;

/// <summary>
///     Repository interface for Shipment aggregate.
///     Follows the Repository pattern for persistence abstraction.
/// </summary>
public interface IShipmentRepository : IRepository<Shipment, Guid>
{
    /// <summary>
    ///     Gets a shipment by Order ID.
    /// </summary>
    Task<Shipment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a shipment by tracking number.
    /// </summary>
    Task<Shipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all shipments for a specific courier that are in transit.
    /// </summary>
    Task<IReadOnlyList<Shipment>> GetInTransitByCourierAsync(string courierProvider, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets shipments that need status updates (in transit for more than X hours).
    /// </summary>
    Task<IReadOnlyList<Shipment>> GetShipmentsNeedingTrackingUpdateAsync(
        TimeSpan minAge,
        int batchSize,
        CancellationToken cancellationToken = default);
}
