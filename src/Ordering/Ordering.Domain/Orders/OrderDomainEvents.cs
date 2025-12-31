using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

// Domain Events for Order aggregate

public sealed record OrderCreatedDomainEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId) : DomainEvent;

public sealed record OrderPaidDomainEvent(
    Guid OrderId,
    string OrderNumber,
    Money TotalAmount) : DomainEvent;

public sealed record OrderProcessingStartedDomainEvent(
    Guid OrderId) : DomainEvent;

public sealed record OrderShippedDomainEvent(
    Guid OrderId,
    string? TrackingNumber) : DomainEvent;

public sealed record OrderDeliveredDomainEvent(
    Guid OrderId) : DomainEvent;

public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    string Reason,
    OrderStatus PreviousStatus) : DomainEvent;
