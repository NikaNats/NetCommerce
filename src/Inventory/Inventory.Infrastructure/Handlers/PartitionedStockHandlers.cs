using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Infrastructure.Handlers;

/// <summary>
///     Partitioned Sequential Messaging handlers for high-contention inventory operations.
///
///     <para>
///     Architecture: These handlers run in the "inventory-contention" local queue which
///     uses message partitioning by ProductId. This means:
///     - All commands for the same ProductId are processed sequentially by the same thread
///     - Different ProductIds can be processed in parallel (up to 9 concurrent tracks)
///     - NO database locks (FOR UPDATE) are needed - thread-level serialization provides safety
///     </para>
///
///     <para>
///     Benefits:
///     - Zero DB deadlocks (Postgres only sees non-conflicting statements)
///     - Maximized CPU utilization (9 products processed in parallel)
///     - Healthy connection pool (only 9 threads ever active in DB for inventory)
///     </para>
/// </summary>
[WolverineHandler]
[LocalQueue("inventory-contention")] // CRITICAL: Run in the partitioned lane
public class PartitionedReserveInventoryHandler
{
    /// <summary>
    ///     Handles inventory reservation from the OrderFulfillmentSaga.
    ///
    ///     <para>
    ///     Thread Safety: Wolverine's message partitioning guarantees that no other thread
    ///     is handling THIS ProductId right now. We can safely read and update stock
    ///     without pessimistic locking.
    ///     </para>
    /// </summary>
    public static async Task<object> Handle(
        ReserveInventoryCommand command,
        InventoryDbContext db,
        ILogger<PartitionedReserveInventoryHandler> logger,
        CancellationToken ct)
    {
        if (command.Items.Count == 0)
        {
            logger.LogWarning(
                "ReserveInventoryCommand for Order {OrderId} has no items",
                command.OrderId);

            return new InventoryReservationFailed(
                command.OrderId,
                "No items to reserve",
                UnavailableProductIds: null);
        }

        var reservedItems = new List<ReservedItem>();
        var unavailableProducts = new List<Guid>();

        foreach (var item in command.Items)
        {
            logger.LogDebug(
                "Processing reservation for Product {ProductId}, Quantity {Quantity}, Order {OrderId}",
                item.ProductId,
                item.Quantity,
                command.OrderId);

            // NO 'FOR UPDATE' needed here!
            // Wolverine guarantees that no other thread is handling THIS ProductId right now.
            // This is the key insight of Partitioned Sequential Messaging.
            var stock = await db.Stocks
                .Include(s => s.Reservations)
                .FirstOrDefaultAsync(s => s.ProductId == item.ProductId, ct);

            if (stock is null)
            {
                logger.LogWarning(
                    "Stock record not found for Product {ProductId}, Order {OrderId}",
                    item.ProductId,
                    command.OrderId);

                unavailableProducts.Add(item.ProductId);
                continue;
            }

            try
            {
                // This logic is now thread-safe by design
                // No concurrent thread can access the same ProductId's stock
                var reservation = stock.Reserve(command.OrderId, item.Quantity);

                // Note: EF Core tracks 'stock' and 'reservation'.
                // Wolverine's AutoApplyTransactions will call SaveChangesAsync automatically.

                reservedItems.Add(new ReservedItem(
                    item.ProductId,
                    reservation.Id,
                    reservation.Quantity));

                logger.LogInformation(
                    "Reserved {Quantity} units of Product {ProductId} for Order {OrderId}. " +
                    "ReservationId: {ReservationId}, Remaining Available: {Available}",
                    item.Quantity,
                    item.ProductId,
                    command.OrderId,
                    reservation.Id,
                    stock.AvailableQuantity);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(
                    "Reservation failed for Product {ProductId}, Order {OrderId}: {Message}",
                    item.ProductId,
                    command.OrderId,
                    ex.Message);

                unavailableProducts.Add(item.ProductId);
            }
        }

        // If any items couldn't be reserved, fail the entire reservation
        if (unavailableProducts.Count > 0)
        {
            // Rollback any partial reservations
            // Since we're in a transaction, the changes won't be committed
            logger.LogWarning(
                "Inventory reservation failed for Order {OrderId}. " +
                "Unavailable products: {Products}",
                command.OrderId,
                string.Join(", ", unavailableProducts));

            return new InventoryReservationFailed(
                command.OrderId,
                $"Insufficient stock for {unavailableProducts.Count} product(s)",
                unavailableProducts);
        }

        logger.LogInformation(
            "Inventory reservation successful for Order {OrderId}. Reserved {Count} items.",
            command.OrderId,
            reservedItems.Count);

        return new InventoryReserved(command.OrderId, reservedItems);
    }
}

/// <summary>
///     Partitioned handler for confirming inventory reservations.
///     Converts soft reservations to hard deductions after payment confirmation.
/// </summary>
[WolverineHandler]
[LocalQueue("inventory-contention")]
public class PartitionedConfirmInventoryHandler
{
    public static async Task<object> Handle(
        ConfirmInventoryCommand command,
        InventoryDbContext db,
        ILogger<PartitionedConfirmInventoryHandler> logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Confirming inventory for Order {OrderId}. PaymentTransactionId: {TransactionId}",
            command.OrderId,
            command.PaymentTransactionId);

        // Find all reservations for this order
        var stocks = await db.Stocks
            .Include(s => s.Reservations)
            .Where(s => s.Reservations.Any(r => r.OrderId == command.OrderId))
            .ToListAsync(ct);

        if (stocks.Count == 0)
        {
            logger.LogWarning(
                "No reservations found for Order {OrderId}",
                command.OrderId);

            return new InventoryConfirmationFailed(
                command.OrderId,
                "No reservations found for this order");
        }

        try
        {
            var confirmedCount = 0;

            foreach (var stock in stocks)
            {
                var reservation = stock.Reservations
                    .FirstOrDefault(r => r.OrderId == command.OrderId &&
                                         r.Status == Domain.Stock.ReservationStatus.Active);

                if (reservation is not null)
                {
                    stock.ConfirmReservation(reservation.Id);
                    confirmedCount++;

                    logger.LogDebug(
                        "Confirmed reservation {ReservationId} for Product {ProductId}, Order {OrderId}",
                        reservation.Id,
                        stock.ProductId,
                        command.OrderId);
                }
            }

            logger.LogInformation(
                "Inventory confirmed for Order {OrderId}. Confirmed {Count} reservations. " +
                "Stock has been permanently deducted.",
                command.OrderId,
                confirmedCount);

            return new InventoryConfirmed(command.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Inventory confirmation failed for Order {OrderId}: {Error}",
                command.OrderId,
                ex.Message);

            return new InventoryConfirmationFailed(command.OrderId, ex.Message);
        }
    }
}

/// <summary>
///     Partitioned handler for releasing inventory reservations.
///     Used as a compensating action when order fails or payment times out.
/// </summary>
[WolverineHandler]
[LocalQueue("inventory-contention")]
public class PartitionedReleaseInventoryHandler
{
    public static async Task Handle(
        ReleaseInventoryReservationCommand command,
        InventoryDbContext db,
        ILogger<PartitionedReleaseInventoryHandler> logger,
        CancellationToken ct)
    {
        logger.LogWarning(
            "Releasing inventory reservation for Order {OrderId}. Reason: {Reason}",
            command.OrderId,
            command.Reason);

        try
        {
            // Find all reservations for this order
            var stocks = await db.Stocks
                .Include(s => s.Reservations)
                .Where(s => s.Reservations.Any(r => r.OrderId == command.OrderId))
                .ToListAsync(ct);

            if (stocks.Count == 0)
            {
                logger.LogInformation(
                    "No reservations found to release for Order {OrderId}",
                    command.OrderId);
                return;
            }

            var releasedCount = 0;

            foreach (var stock in stocks)
            {
                var reservation = stock.Reservations
                    .FirstOrDefault(r => r.OrderId == command.OrderId &&
                                         r.Status == Domain.Stock.ReservationStatus.Active);

                if (reservation is not null)
                {
                    stock.ReleaseReservation(reservation.Id);
                    releasedCount++;

                    logger.LogDebug(
                        "Released reservation {ReservationId} for Product {ProductId}, Order {OrderId}",
                        reservation.Id,
                        stock.ProductId,
                        command.OrderId);
                }
            }

            logger.LogInformation(
                "Inventory reservation released for Order {OrderId}. Released {Count} reservations. " +
                "Stock is now available again.",
                command.OrderId,
                releasedCount);
        }
        catch (Exception ex)
        {
            // Log but don't throw - this is a compensating action
            // Manual intervention may be needed
            logger.LogCritical(ex,
                "CRITICAL: Failed to release inventory reservation for Order {OrderId}. " +
                "Manual intervention required to prevent stock discrepancy! Reason: {Reason}",
                command.OrderId,
                command.Reason);
        }
    }
}
