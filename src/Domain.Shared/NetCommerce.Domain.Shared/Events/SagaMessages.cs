#nullable enable
using NetCommerce.Kernel.Application;
using Wolverine;
using Wolverine.Persistence.Sagas;

namespace NetCommerce.Domain.Shared.Events;

#region Saga Commands - Messages sent TO modules

/// <summary>
///     Command sent by the OrderSaga to request payment processing.
/// </summary>
public sealed record RequestPaymentCommand(
    Guid OrderId,
    Guid CustomerId,
    Money Amount,
    string OrderNumber) : ICommand;

/// <summary>
///     Command sent by webhook endpoint to process external payment confirmation.
/// </summary>
public sealed record ProcessExternalPaymentConfirmation(
    string ExternalTransactionId,
    string Status,
    string WebhookEventId) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to reserve inventory.
/// </summary>
public sealed record ReserveInventoryCommand(
    Guid OrderId,
    IReadOnlyList<OrderItemReservation> Items) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to lock existing reservations for payment.
/// </summary>
public sealed record LockInventoryForPaymentCommand(
    Guid OrderId,
    IReadOnlyList<ReservedItem> ReservedItems) : ICommand;

/// <summary>
///     Item details for inventory reservation.
/// </summary>
public sealed record OrderItemReservation(
    Guid ProductId,
    int Quantity,
    string? Sku);

/// <summary>
///     Command sent by the OrderSaga to confirm inventory after payment.
/// </summary>
public sealed record ConfirmInventoryCommand(
    Guid OrderId,
    string PaymentTransactionId) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to release inventory reservation.
/// </summary>
public sealed record ReleaseInventoryReservationCommand(
    Guid OrderId,
    string Reason) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to request a refund.
/// </summary>
public sealed record RefundPaymentCommand(
    Guid OrderId,
    string PaymentTransactionId,
    Money Amount,
    string Reason) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to finalize the order.
/// </summary>
public sealed record FinalizeOrderCommand(
    Guid OrderId,
    string PaymentTransactionId) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to fail the order.
/// </summary>
public sealed record FailOrderCommand(
    Guid OrderId,
    string FailureReason) : ICommand;

#endregion

#region Saga Events - Messages received FROM modules

/// <summary>
///     Event published when payment is initiated.
/// </summary>
public sealed record PaymentInitiated(
    [property: SagaIdentity] Guid OrderId,
    Guid PaymentTransactionId,
    string ExternalTransactionId,
    Money Amount) : IntegrationEvent;

/// <summary>
///     Event published when payment succeeds.
/// </summary>
public sealed record PaymentSucceeded(
    [property: SagaIdentity] Guid OrderId,
    string ExternalTransactionId,
    Money Amount) : IntegrationEvent;

/// <summary>
///     Event published when payment fails.
/// </summary>
public sealed record PaymentFailed(
    [property: SagaIdentity] Guid OrderId,
    string Reason,
    string? ErrorCode) : IntegrationEvent;

/// <summary>
///     Event published when inventory reservation succeeds.
/// </summary>
public sealed record InventoryReserved(
    [property: SagaIdentity] Guid OrderId,
    IReadOnlyList<ReservedItem> ReservedItems) : IntegrationEvent;

/// <summary>
///     Event published after reservations are promoted to payment locks.
/// </summary>
public sealed record InventoryLocked(
    [property: SagaIdentity] Guid OrderId,
    IReadOnlyList<ReservedItem> ReservedItems) : IntegrationEvent;

/// <summary>
///     Reserved item details.
/// </summary>
public sealed record ReservedItem(
    Guid ProductId,
    Guid ReservationId,
    int Quantity);

/// <summary>
///     Event published when inventory reservation fails.
/// </summary>
public sealed record InventoryReservationFailed(
    [property: SagaIdentity] Guid OrderId,
    string Reason,
    IReadOnlyList<Guid>? UnavailableProductIds) : IntegrationEvent;

/// <summary>
///     Event published when inventory is confirmed.
/// </summary>
public sealed record InventoryConfirmed(
    [property: SagaIdentity] Guid OrderId) : IntegrationEvent;

/// <summary>
///     Event published when inventory confirmation fails.
/// </summary>
public sealed record InventoryConfirmationFailed(
    [property: SagaIdentity] Guid OrderId,
    string Reason) : IntegrationEvent;

/// <summary>
///     Event published when refund completes.
/// </summary>
public sealed record RefundCompleted(
    [property: SagaIdentity] Guid OrderId,
    Guid RefundTransactionId,
    Money Amount) : IntegrationEvent;

/// <summary>
///     Event published when refund fails.
/// </summary>
public sealed record RefundFailed(
    [property: SagaIdentity] Guid OrderId,
    string Reason) : IntegrationEvent;

#endregion

#region Timeout Messages

public sealed record PaymentTimeoutMessage : TimeoutMessage
{
    public PaymentTimeoutMessage() : base(TimeSpan.FromMinutes(30)) { }
    public Guid Id { get; init; }
}

public sealed record InventoryReservationTimeoutMessage : TimeoutMessage
{
    public InventoryReservationTimeoutMessage() : base(TimeSpan.FromMinutes(5)) { }
    public Guid Id { get; init; }
}

public sealed record GracePeriodTimeout : TimeoutMessage
{
    public GracePeriodTimeout() : base(TimeSpan.FromMinutes(5)) { }
    public Guid Id { get; init; }
}

public sealed record InventoryConfirmationTimeoutMessage : TimeoutMessage
{
    public InventoryConfirmationTimeoutMessage() : base(TimeSpan.FromMinutes(5)) { }
    public Guid Id { get; init; }
}

#endregion

#region Saga Initiation Command

public sealed record StartOrderFulfillmentCommand(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    Money TotalAmount,
    IReadOnlyList<OrderItemReservation> Items) : ICommand;

#endregion

#region Shipping Integration Events

public sealed record OrderReadyForShipping(
    Guid OrderId,
    string OrderNumber,
    IReadOnlyList<ShippingItem> Items,
    ShippingAddressDto Address) : IntegrationEvent;

public sealed record ShippingItem(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal WeightKg);

public sealed record ShippingAddressDto(
    string RecipientName,
    string Street,
    string City,
    string State,
    string Country,
    string PostalCode,
    string Phone);

public sealed record ShipmentCreatedIntegrationEvent(
    Guid OrderId,
    Guid ShipmentId,
    string TrackingNumber,
    string CourierProvider,
    DateTime? EstimatedDeliveryDate) : IntegrationEvent;

/// <summary>
///     Published when shipping label creation fails.
///     Allows Ordering module to handle the failure gracefully.
/// </summary>
public sealed record ShipmentCreationFailedEvent(
    Guid OrderId,
    string ErrorCode,
    string ErrorMessage) : IntegrationEvent;

public sealed record ShipmentDeliveredIntegrationEvent(
    Guid OrderId,
    Guid ShipmentId,
    string TrackingNumber,
    DateTime DeliveredAt) : IntegrationEvent;

#endregion
