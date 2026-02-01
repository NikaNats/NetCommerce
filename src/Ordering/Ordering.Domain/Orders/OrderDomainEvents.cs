using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

// Domain Events for Order aggregate

/// <summary>
///     Raised when an order is submitted.
///     Triggers soft reservation in Inventory module.
/// </summary>
public sealed record OrderSubmittedDomainEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId) : DomainEvent;

/// <summary>
///     Raised when the grace period ends for an order.
///     Triggers payment processing.
/// </summary>
public sealed record OrderGracePeriodConfirmedDomainEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Money TotalAmount) : DomainEvent;

/// <summary>
///     Raised when stock is confirmed for an order.
/// </summary>
public sealed record OrderStockConfirmedDomainEvent(
    Guid OrderId) : DomainEvent;

public sealed record OrderPaidDomainEvent(
    Guid OrderId,
    string PaymentTransactionId,
    string OrderNumber,
    Money TotalAmount) : DomainEvent;

public sealed record OrderShippedDomainEvent(
    Guid OrderId,
    string? TrackingNumber) : DomainEvent;

public sealed record OrderDeliveredDomainEvent(
    Guid OrderId) : DomainEvent;

public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    string Reason,
    OrderStatus PreviousStatus) : DomainEvent;

/// <summary>
///     Raised when a shadow order is created during financial reconciliation.
///     Shadow orders are created to account for "ghost charges" - payments that exist
///     in the PSP but have no corresponding internal order record.
///     This event is for audit purposes only - no inventory or payment processing needed.
/// </summary>
public sealed record ShadowOrderCreatedDomainEvent(
    Guid OrderId,
    string OrderNumber,
    string ExternalTxnId,
    Money Amount,
    string ResolvedBy) : DomainEvent;
