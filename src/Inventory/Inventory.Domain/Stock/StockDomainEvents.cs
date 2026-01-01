using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Inventory.Domain.Stock;

// Domain Events for Stock aggregate

public sealed record StockReservedDomainEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int RemainingAvailable) : DomainEvent;

public sealed record StockDeductedDomainEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int NewTotal) : DomainEvent;

public sealed record StockReleasedDomainEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int NewAvailable) : DomainEvent;

public sealed record StockAddedDomainEvent(
    Guid StockId,
    Guid ProductId,
    int Quantity,
    int NewTotal,
    string? Reason) : DomainEvent;

public sealed record StockRemovedDomainEvent(
    Guid StockId,
    Guid ProductId,
    int Quantity,
    int NewTotal,
    string Reason) : DomainEvent;

public sealed record LowStockAlertDomainEvent(
    Guid StockId,
    Guid ProductId,
    string Sku,
    int CurrentQuantity) : DomainEvent;

public sealed record ReservationExpiredDomainEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity) : DomainEvent;