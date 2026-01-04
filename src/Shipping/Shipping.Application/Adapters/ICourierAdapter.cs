using NetCommerce.Shipping.Domain;

namespace NetCommerce.Shipping.Application.Adapters;

/// <summary>
///     Interface for courier service adapters.
///     Implements the Adapter Pattern for multiple shipping providers.
/// </summary>
public interface ICourierAdapter
{
    /// <summary>
    ///     The name of the courier (e.g., "DHL", "FedEx", "UPS").
    /// </summary>
    string CourierName { get; }

    /// <summary>
    ///     Creates a shipping label and returns tracking information.
    /// </summary>
    Task<CourierLabelResult> CreateLabelAsync(
        Address shippingAddress,
        decimal weightKg,
        ShipmentDimensions dimensions,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Cancels a shipping label.
    /// </summary>
    Task<bool> CancelLabelAsync(
        string trackingNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the latest tracking status from the courier.
    /// </summary>
    Task<CourierTrackingStatus> GetTrackingStatusAsync(
        string trackingNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Result from creating a shipping label.
/// </summary>
public sealed record CourierLabelResult(
    string TrackingNumber,
    string LabelUrl,
    decimal ShippingCost,
    string Currency,
    DateTime? EstimatedDeliveryDate);

/// <summary>
///     Tracking status from courier.
/// </summary>
public sealed record CourierTrackingStatus(
    string TrackingNumber,
    string Status,
    string StatusDescription,
    DateTime LastUpdated,
    bool IsDelivered);
