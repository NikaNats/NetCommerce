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
        // Execution-strategy transaction, mirroring ReserveInventoryHandler. The
        // generated Wolverine pipeline opens NO transaction before this handler, so
        // without one the SELECT ... FOR UPDATE below runs in autocommit and releases
        // its row lock immediately: concurrent reservers for the same product then
        // race on the xmin check and all but one fail with 409 (measured: 30 of 5,000
        // succeeded against ample stock). Inside the transaction they serialize.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // A transient-fault retry re-runs this lambda on the same context. Without
            // clearing, the re-query returns the already-tracked stock carrying the
            // failed attempt's reservation, and the retry would persist two.
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Lock first, load second: see StockRowLocks for why one statement oversells.
            await StockRowLocks.LockAsync(db, [command.ProductId], cancellationToken);

            var stock = await db.Stocks
                .Where(s => s.ProductId == command.ProductId)
                // Only non-terminal reservations: Reserve never reads Confirmed/Released
                // history, which is never pruned and would otherwise be loaded in full
                // on every reservation of a popular product, under the row lock.
                .Include(s => s.Reservations.Where(r =>
                    r.Status == Domain.Stock.ReservationStatus.Active ||
                    r.Status == Domain.Stock.ReservationStatus.PendingPayment))
                .FirstOrDefaultAsync(cancellationToken);

            if (stock is null)
                return Result.Failure<Guid>(
                    Error.NotFound("Stock", command.ProductId));

            Domain.Stock.StockReservation reservation;
            try
            {
                reservation = stock.Reserve(command.OrderId, command.Quantity);
            }
            catch (InvalidOperationException ex)
            {
                // Business rejection (e.g. insufficient stock): nothing to persist; the
                // disposed transaction rolls back and releases the lock.
                return Result.Failure<Guid>(Error.Conflict(ex.Message));
            }

            logger.LogInformation(
                "Reserved {Quantity} units for order {OrderId}, reservation {ReservationId}",
                command.Quantity, command.OrderId, reservation.Id);

            // Save and commit inside the handler: the lock must be held until the write
            // lands. The Wolverine postprocessor SaveChanges afterwards is a no-op, and
            // its CurrentTransaction commit is skipped because ours is already complete.
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success(reservation.Id);
        });
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
