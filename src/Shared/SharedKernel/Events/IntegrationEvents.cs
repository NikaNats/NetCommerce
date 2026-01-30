// =============================================================================
// DEPRECATED: Use NetCommerce.Domain.Shared.Events instead.
// This file exists for backward compatibility during migration.
// All integration events should use the canonical types from Domain.Shared.
// =============================================================================
#pragma warning disable CS0618 // Suppress obsolete warnings - this file uses deprecated Money type

using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Events;

/// <summary>
///     Integration events for cross-module communication.
///     These are mirrors of domain events published in different modules.
///     They allow modules to communicate without direct coupling.
/// </summary>
/// <remarks>
///     DEPRECATED: Use types from <c>NetCommerce.Domain.Shared.Events</c> namespace instead.
/// </remarks>

/// <summary>
///     Raised when an order is submitted.
///     Triggers soft stock reservation in Inventory module.
/// </summary>
[Obsolete("Use NetCommerce.Domain.Shared.Events.OrderSubmittedIntegrationEvent instead.")]
public sealed record OrderSubmittedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId) : IntegrationEvent;

/// <summary>
///     Raised when the grace period ends for an order.
///     Triggers payment capture in Payments module.
/// </summary>
[Obsolete("Use NetCommerce.Domain.Shared.Events.OrderGracePeriodConfirmedIntegrationEvent instead.")]
public sealed record OrderGracePeriodConfirmedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Money TotalAmount) : IntegrationEvent;

/// <summary>
///     Raised when stock is confirmed for an order.
/// </summary>
[Obsolete("Use NetCommerce.Domain.Shared.Events.OrderStockConfirmedIntegrationEvent instead.")]
public sealed record OrderStockConfirmedIntegrationEvent(
    Guid OrderId) : IntegrationEvent;

[Obsolete("Use NetCommerce.Domain.Shared.Events.OrderPaidIntegrationEvent instead.")]
public sealed record OrderPaidIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Money TotalAmount) : IntegrationEvent;

/// <summary>
///     Raised when an order is successfully placed (after all validations).
///     Triggers email/SMS notifications to customer.
/// </summary>
[Obsolete("Use NetCommerce.Domain.Shared.Events.OrderPlacedIntegrationEvent instead.")]
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
[Obsolete("Use NetCommerce.Domain.Shared.Events.OrderCancelledIntegrationEvent instead.")]
public sealed record OrderCancelledIntegrationEvent(
    Guid OrderId,
    string Reason,
    string PreviousStatus) : IntegrationEvent;

[Obsolete("Use NetCommerce.Domain.Shared.Events.PaymentCompletedIntegrationEvent instead.")]
public sealed record PaymentCompletedIntegrationEvent(
    string ExternalTransactionId,
    Guid OrderId,
    Money Amount) : IntegrationEvent;

/// <summary>
///     Raised when inventory confirmation cannot be completed after a successful payment
///     and the originating outbox message has exhausted retries.
///     Used to trigger compensating actions (e.g., refund) or support alerting.
/// </summary>
[Obsolete("Use NetCommerce.Domain.Shared.Events.OrderInventoryConfirmationFailedIntegrationEvent instead.")]
public sealed record OrderInventoryConfirmationFailedIntegrationEvent(
    Guid OrderId,
    string PaymentTransactionId,
    Money Amount,
    string FailureReason,
    string? FailureDetails) : IntegrationEvent;

[Obsolete("Use NetCommerce.Domain.Shared.Events.StockReservedIntegrationEvent instead.")]
public sealed record StockReservedIntegrationEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int RemainingAvailable) : IntegrationEvent;

[Obsolete("Use NetCommerce.Domain.Shared.Events.StockDeductedIntegrationEvent instead.")]
public sealed record StockDeductedIntegrationEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int NewTotal) : IntegrationEvent;

/// <summary>
///     Raised when a stock reservation is released (e.g., order cancelled during grace period).
/// </summary>
[Obsolete("Use NetCommerce.Domain.Shared.Events.StockReservationReleasedIntegrationEvent instead.")]
public sealed record StockReservationReleasedIntegrationEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int NewAvailable) : IntegrationEvent;
