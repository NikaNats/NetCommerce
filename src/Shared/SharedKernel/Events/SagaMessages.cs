using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using Wolverine;
using Wolverine.Persistence.Sagas;

namespace NetCommerce.SharedKernel.Events;

#region Saga Commands - Messages sent TO modules

/// <summary>
///     Command sent by the OrderSaga to request payment processing.
///     Targets the Payments module.
/// </summary>
public sealed record RequestPaymentCommand(
    Guid OrderId,
    Guid CustomerId,
    Money Amount,
    string OrderNumber) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to reserve inventory.
///     Targets the Inventory module.
/// </summary>
public sealed record ReserveInventoryCommand(
    Guid OrderId,
    IReadOnlyList<OrderItemReservation> Items) : ICommand;

/// <summary>
///     Item details for inventory reservation.
/// </summary>
public sealed record OrderItemReservation(
    Guid ProductId,
    int Quantity,
    string? Sku);

/// <summary>
///     Command sent by the OrderSaga to confirm inventory after payment.
///     Targets the Inventory module.
/// </summary>
public sealed record ConfirmInventoryCommand(
    Guid OrderId,
    Guid PaymentTransactionId) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to release inventory reservation.
///     Used as a compensating action when the saga fails.
///     Targets the Inventory module.
/// </summary>
public sealed record ReleaseInventoryReservationCommand(
    Guid OrderId,
    string Reason) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to request a refund.
///     Used as a compensating action when inventory confirmation fails.
///     Targets the Payments module.
/// </summary>
public sealed record RefundPaymentCommand(
    Guid OrderId,
    Guid PaymentTransactionId,
    Money Amount,
    string Reason) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to finalize the order.
///     Targets the Ordering domain.
/// </summary>
public sealed record FinalizeOrderCommand(
    Guid OrderId,
    Guid PaymentTransactionId) : ICommand;

/// <summary>
///     Command sent by the OrderSaga to fail the order.
///     Targets the Ordering domain.
/// </summary>
public sealed record FailOrderCommand(
    Guid OrderId,
    string FailureReason) : ICommand;

#endregion

#region Saga Events - Messages received FROM modules

/// <summary>
///     Event published by the Payments module when payment succeeds.
/// </summary>
public sealed record PaymentSucceeded(
    [property: SagaIdentity] Guid OrderId,
    Guid TransactionId,
    Money Amount) : IntegrationEvent;

/// <summary>
///     Event published by the Payments module when payment fails.
/// </summary>
public sealed record PaymentFailed(
    [property: SagaIdentity] Guid OrderId,
    string Reason,
    string? ErrorCode) : IntegrationEvent;

/// <summary>
///     Event published by the Inventory module when reservation succeeds.
/// </summary>
public sealed record InventoryReserved(
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
///     Event published by the Inventory module when reservation fails.
/// </summary>
public sealed record InventoryReservationFailed(
    [property: SagaIdentity] Guid OrderId,
    string Reason,
    IReadOnlyList<Guid>? UnavailableProductIds) : IntegrationEvent;

/// <summary>
///     Event published by the Inventory module when inventory is confirmed.
/// </summary>
public sealed record InventoryConfirmed(
    [property: SagaIdentity] Guid OrderId) : IntegrationEvent;

/// <summary>
///     Event published by the Inventory module when inventory confirmation fails.
/// </summary>
public sealed record InventoryConfirmationFailed(
    [property: SagaIdentity] Guid OrderId,
    string Reason) : IntegrationEvent;

/// <summary>
///     Event published by the Payments module when refund completes.
/// </summary>
public sealed record RefundCompleted(
    [property: SagaIdentity] Guid OrderId,
    Guid RefundTransactionId,
    Money Amount) : IntegrationEvent;

/// <summary>
///     Event published by the Payments module when refund fails.
/// </summary>
public sealed record RefundFailed(
    [property: SagaIdentity] Guid OrderId,
    string Reason) : IntegrationEvent;

#endregion

#region Timeout Messages

/// <summary>
///     Timeout message for payment processing.
///     If payment is not received within the timeout period, the saga cancels the order.
/// </summary>
public sealed record PaymentTimeoutMessage : TimeoutMessage
{
    public PaymentTimeoutMessage() : base(TimeSpan.FromMinutes(30))
    {
    }

    /// <summary>
    ///     The OrderId is used by Wolverine to find the saga instance.
    /// </summary>
    public Guid Id { get; init; }
}

/// <summary>
///     Timeout message for inventory reservation.
///     If inventory is not reserved within the timeout period, the saga cancels.
/// </summary>
public sealed record InventoryReservationTimeoutMessage : TimeoutMessage
{
    public InventoryReservationTimeoutMessage() : base(TimeSpan.FromMinutes(5))
    {
    }

    /// <summary>
    ///     The OrderId is used by Wolverine to find the saga instance.
    /// </summary>
    public Guid Id { get; init; }
}

/// <summary>
///     Timeout message for inventory confirmation.
/// </summary>
public sealed record InventoryConfirmationTimeoutMessage : TimeoutMessage
{
    public InventoryConfirmationTimeoutMessage() : base(TimeSpan.FromMinutes(5))
    {
    }

    public Guid Id { get; init; }
}

#endregion

#region Saga Initiation Command

/// <summary>
///     Command to start the order fulfillment saga.
///     Sent when an order's grace period ends.
/// </summary>
public sealed record StartOrderFulfillmentCommand(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    Money TotalAmount,
    IReadOnlyList<OrderItemReservation> Items) : ICommand;

#endregion
