using NetCommerce.SharedKernel.Application;

namespace NetCommerce.Inventory.Application.Stock.Commands;

/// <summary>
///     Command to create a new stock record for a product.
/// </summary>
public record CreateStockCommand(
    Guid ProductId,
    string Sku,
    int InitialQuantity,
    int LowStockThreshold = 10,
    string? WarehouseLocation = null) : ICommand<Guid>;

/// <summary>
///     Command to reserve stock for an order.
///     Uses database-level pessimistic locking (FOR UPDATE) to prevent overselling.
/// </summary>
public record ReserveStockCommand(
    Guid ProductId,
    Guid OrderId,
    int Quantity) : ICommand<Guid>;

/// <summary>
///     Command to update stock quantity.
/// </summary>
public record UpdateStockQuantityCommand(
    Guid StockId,
    int QuantityDelta,
    string Reason) : ICommand;

/// <summary>
///     Command to confirm a stock reservation after payment.
/// </summary>
public record ConfirmReservationCommand(
    Guid ProductId,
    Guid ReservationId) : ICommand;

/// <summary>
///     Command to release a stock reservation.
/// </summary>
public record ReleaseReservationCommand(
    Guid ProductId,
    Guid ReservationId) : ICommand;
