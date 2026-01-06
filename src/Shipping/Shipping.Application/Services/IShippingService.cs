using NetCommerce.Kernel.Application;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Kernel.Core.Results;

namespace NetCommerce.Shipping.Application.Services;

/// <summary>
///     Service interface for shipping operations.
/// </summary>
public interface IShippingService
{
    /// <summary>
    ///     Creates a shipping label using the specified courier.
    /// </summary>
    Task<Result<ShippingLabelDto>> CreateLabelAsync(
        Guid orderId,
        string orderNumber,
        ShippingAddressDto address,
        IReadOnlyList<ShippingItemDto> items,
        string? preferredCourier = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     DTO for shipping label creation result.
/// </summary>
public sealed record ShippingLabelDto(
    Guid ShipmentId,
    string TrackingNumber,
    string CourierProvider,
    string LabelUrl,
    decimal ShippingCost,
    DateTime? EstimatedDeliveryDate);

/// <summary>
///     DTO for shipping items (local to Shipping module).
/// </summary>
public sealed record ShippingItemDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal WeightKg);
