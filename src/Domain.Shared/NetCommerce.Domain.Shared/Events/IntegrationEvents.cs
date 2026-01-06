#nullable enable
using NetCommerce.Kernel.Application;

namespace NetCommerce.Domain.Shared.Events;

/// <summary>
///     Integration events for cross-module communication.
///     These are mirrors of domain events published in different modules.
///     They allow modules to communicate without direct coupling.
/// </summary>

/// <summary>
///     Raised when an order is submitted.
///     Triggers soft stock reservation in Inventory module.
/// </summary>
public sealed record OrderSubmittedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId) : IntegrationEvent;

/// <summary>
///     Raised when the grace period ends for an order.
///     Triggers payment capture in Payments module.
/// </summary>
public sealed record OrderGracePeriodConfirmedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Money TotalAmount) : IntegrationEvent;

/// <summary>
///     Raised when stock is confirmed for an order.
/// </summary>
public sealed record OrderStockConfirmedIntegrationEvent(
    Guid OrderId) : IntegrationEvent;

public sealed record OrderPaidIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Money TotalAmount) : IntegrationEvent;

/// <summary>
///     Raised when an order is successfully placed (after all validations).
///     Triggers email/SMS notifications to customer.
/// </summary>
public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    string CustomerEmail,
    string CustomerName,
    Money TotalAmount) : IntegrationEvent;

/// <summary>
///     Raised when an order is cancelled.
///     PreviousStatus indicates whether payment was taken (requires refund).
/// </summary>
public sealed record OrderCancelledIntegrationEvent(
    Guid OrderId,
    string Reason,
    string PreviousStatus) : IntegrationEvent;

public sealed record PaymentCompletedIntegrationEvent(
    string ExternalTransactionId,
    Guid OrderId,
    Money Amount) : IntegrationEvent;

/// <summary>
///     Raised when inventory confirmation cannot be completed after a successful payment
///     and the originating outbox message has exhausted retries.
///     Used to trigger compensating actions (e.g., refund) or support alerting.
/// </summary>
public sealed record OrderInventoryConfirmationFailedIntegrationEvent(
    Guid OrderId,
    string PaymentTransactionId,
    Money Amount,
    string FailureReason,
    string? FailureDetails) : IntegrationEvent;

public sealed record StockReservedIntegrationEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int RemainingAvailable) : IntegrationEvent;

public sealed record StockDeductedIntegrationEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int NewTotal) : IntegrationEvent;

/// <summary>
///     Raised when a stock reservation is released (e.g., order cancelled during grace period).
/// </summary>
public sealed record StockReservationReleasedIntegrationEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int NewAvailable) : IntegrationEvent;
