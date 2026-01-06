using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Kernel.Core.Results;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Infrastructure.Handlers;

/// <summary>
///     Wolverine handler for CreateStockCommand.
/// </summary>
[WolverineHandler]
public static class CreateStockHandler
{
    public static async Task<Result<Guid>> HandleAsync(
        CreateStockCommand command,
        InventoryDbContext db,
        ILogger<CreateStockCommand> logger,
        CancellationToken cancellationToken)
    {
        // Check if stock already exists for this product
        var exists = await db.Stocks
            .AnyAsync(s => s.ProductId == command.ProductId, cancellationToken);

        if (exists)
            return Result.Failure<Guid>(
                Error.Conflict($"Stock already exists for product {command.ProductId}"));

        var stock = Domain.Stock.Stock.Create(
            command.ProductId,
            command.Sku,
            command.InitialQuantity,
            command.LowStockThreshold,
            command.WarehouseLocation);

        db.Stocks.Add(stock);

        logger.LogInformation(
            "Stock {StockId} created for product {ProductId}",
            stock.Id, command.ProductId);

        return stock.Id;
    }
}

/// <summary>
///     Wolverine handler for ReserveStockCommand.
///     Uses pessimistic locking for concurrency control.
/// </summary>
[WolverineHandler]
public static class ReserveStockHandler
{
    public static async Task<Result<Guid>> HandleAsync(
        ReserveStockCommand command,
        InventoryDbContext db,
        ILogger<ReserveStockCommand> logger,
        CancellationToken cancellationToken)
    {
        // Use pessimistic locking (SELECT FOR UPDATE) via raw SQL
        var stock = await db.Stocks
            .FromSqlInterpolated(
                $"SELECT * FROM inventory.stocks WHERE product_id = {command.ProductId} FOR UPDATE")
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(cancellationToken);

        if (stock is null)
            return Result.Failure<Guid>(
                Error.NotFound("Stock", command.ProductId));

        try
        {
            var reservation = stock.Reserve(command.OrderId, command.Quantity);

            logger.LogInformation(
                "Reserved {Quantity} units for order {OrderId}, reservation {ReservationId}",
                command.Quantity, command.OrderId, reservation.Id);

            return reservation.Id;
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<Guid>(Error.Conflict(ex.Message));
        }
    }
}

/// <summary>
///     Wolverine handler for UpdateStockQuantityCommand.
/// </summary>
[WolverineHandler]
public static class UpdateStockQuantityHandler
{
    public static async Task<Result> HandleAsync(
        UpdateStockQuantityCommand command,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var stock = await db.Stocks.FindAsync([command.StockId], cancellationToken);

        if (stock is null)
            return Result.Failure(Error.NotFound("Stock", command.StockId));

        if (command.QuantityDelta > 0)
            stock.AddStock(command.QuantityDelta, command.Reason);
        else if (command.QuantityDelta < 0)
            stock.RemoveStock(Math.Abs(command.QuantityDelta), command.Reason);

        return Result.Success();
    }
}

/// <summary>
///     Wolverine handler for ConfirmReservationCommand.
/// </summary>
[WolverineHandler]
public static class ConfirmReservationHandler
{
    public static async Task<Result> HandleAsync(
        ConfirmReservationCommand command,
        InventoryDbContext db,
        ILogger<ConfirmReservationCommand> logger,
        CancellationToken cancellationToken)
    {
        var stock = await db.Stocks
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.ProductId == command.ProductId, cancellationToken);

        if (stock is null)
            return Result.Failure(Error.NotFound("Stock", command.ProductId));

        try
        {
            stock.ConfirmReservation(command.ReservationId);

            logger.LogInformation(
                "Reservation {ReservationId} confirmed for product {ProductId}",
                command.ReservationId, command.ProductId);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }
    }
}

/// <summary>
///     Wolverine handler for ReleaseReservationCommand.
/// </summary>
[WolverineHandler]
public static class ReleaseReservationHandler
{
    public static async Task<Result> HandleAsync(
        ReleaseReservationCommand command,
        InventoryDbContext db,
        ILogger<ReleaseReservationCommand> logger,
        CancellationToken cancellationToken)
    {
        var stock = await db.Stocks
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.ProductId == command.ProductId, cancellationToken);

        if (stock is null)
            return Result.Failure(Error.NotFound("Stock", command.ProductId));

        stock.ReleaseReservation(command.ReservationId);

        logger.LogInformation(
            "Reservation {ReservationId} released for product {ProductId}",
            command.ReservationId, command.ProductId);

        return Result.Success();
    }
}
