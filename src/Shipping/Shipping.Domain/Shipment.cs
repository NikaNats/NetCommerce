using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Shipping.Domain;

/// <summary>
///     Shipment aggregate root - manages the physical delivery process.
///     Tracks the lifecycle from label creation to final delivery.
/// </summary>
public sealed class Shipment : AggregateRoot<Guid>
{
    private Shipment()
    {
    }

    /// <summary>
    ///     The Order this shipment belongs to.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    ///     Tracking number from the courier (DHL, FedEx, etc.).
    /// </summary>
    public string TrackingNumber { get; private set; } = string.Empty;

    /// <summary>
    ///     Courier provider handling this shipment.
    /// </summary>
    public string CourierProvider { get; private set; } = string.Empty;

    /// <summary>
    ///     Package weight in kilograms.
    /// </summary>
    public decimal WeightKg { get; private set; }

    /// <summary>
    ///     Package dimensions in centimeters.
    /// </summary>
    public ShipmentDimensions Dimensions { get; private set; } = default!;

    /// <summary>
    ///     Current status of the shipment.
    /// </summary>
    public ShipmentStatus Status { get; private set; }

    /// <summary>
    ///     Shipping address.
    /// </summary>
    public Address ShippingAddress { get; private set; } = default!;

    /// <summary>
    ///     When the shipment label was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    ///     When the shipment was picked up by courier.
    /// </summary>
    public DateTime? PickedUpAt { get; private set; }

    /// <summary>
    ///     When the shipment was delivered.
    /// </summary>
    public DateTime? DeliveredAt { get; private set; }

    /// <summary>
    ///     Estimated delivery date provided by courier.
    /// </summary>
    public DateTime? EstimatedDeliveryDate { get; private set; }

    /// <summary>
    ///     Failure reason if shipment failed.
    /// </summary>
    public string? FailureReason { get; private set; }

    public static Shipment Create(
        Guid orderId,
        string trackingNumber,
        string courierProvider,
        Address shippingAddress,
        decimal weightKg,
        ShipmentDimensions dimensions,
        DateTime? estimatedDeliveryDate = null)
    {
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            TrackingNumber = trackingNumber,
            CourierProvider = courierProvider,
            ShippingAddress = shippingAddress,
            WeightKg = weightKg,
            Dimensions = dimensions,
            Status = ShipmentStatus.LabelCreated,
            CreatedAt = DateTime.UtcNow,
            EstimatedDeliveryDate = estimatedDeliveryDate
        };

        shipment.RaiseDomainEvent(new ShipmentCreatedDomainEvent(
            shipment.Id,
            orderId,
            trackingNumber,
            courierProvider));

        return shipment;
    }

    public void MarkPickedUp()
    {
        if (Status != ShipmentStatus.LabelCreated)
            throw new InvalidOperationException($"Cannot mark as picked up from status {Status}");

        Status = ShipmentStatus.InTransit;
        PickedUpAt = DateTime.UtcNow;

        RaiseDomainEvent(new ShipmentPickedUpDomainEvent(Id, OrderId));
    }

    public void MarkDelivered()
    {
        if (Status != ShipmentStatus.InTransit)
            throw new InvalidOperationException($"Cannot mark as delivered from status {Status}");

        Status = ShipmentStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;

        RaiseDomainEvent(new ShipmentDeliveredDomainEvent(Id, OrderId, TrackingNumber));
    }

    public void MarkFailed(string reason)
    {
        Status = ShipmentStatus.Failed;
        FailureReason = reason;

        RaiseDomainEvent(new ShipmentFailedDomainEvent(Id, OrderId, reason));
    }

    public void UpdateEstimatedDelivery(DateTime newEstimate)
    {
        EstimatedDeliveryDate = newEstimate;
    }
}

/// <summary>
///     Shipment status.
/// </summary>
public enum ShipmentStatus
{
    LabelCreated = 0,
    InTransit = 1,
    Delivered = 2,
    Failed = 3
}

/// <summary>
///     Package dimensions value object.
/// </summary>
public sealed record ShipmentDimensions(
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm)
{
    public decimal VolumeCubicCm => LengthCm * WidthCm * HeightCm;
}

/// <summary>
///     Address value object for shipping.
/// </summary>
public sealed record Address(
    string RecipientName,
    string Street,
    string City,
    string State,
    string Country,
    string PostalCode,
    string Phone);
