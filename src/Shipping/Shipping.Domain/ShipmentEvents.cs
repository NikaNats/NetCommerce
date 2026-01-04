using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Shipping.Domain;

/// <summary>
///     Domain event raised when a shipment is created.
/// </summary>
public sealed record ShipmentCreatedDomainEvent(
    Guid ShipmentId,
    Guid OrderId,
    string TrackingNumber,
    string CourierProvider) : DomainEvent;

/// <summary>
///     Domain event raised when a shipment is picked up by courier.
/// </summary>
public sealed record ShipmentPickedUpDomainEvent(
    Guid ShipmentId,
    Guid OrderId) : DomainEvent;

/// <summary>
///     Domain event raised when a shipment is delivered.
/// </summary>
public sealed record ShipmentDeliveredDomainEvent(
    Guid ShipmentId,
    Guid OrderId,
    string TrackingNumber) : DomainEvent;

/// <summary>
///     Domain event raised when a shipment fails.
/// </summary>
public sealed record ShipmentFailedDomainEvent(
    Guid ShipmentId,
    Guid OrderId,
    string Reason) : DomainEvent;
