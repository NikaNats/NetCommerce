using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Domain.Shared.Events;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Infrastructure.Handlers;

/// <summary>
///     Inventory reservation handler using deterministic, multi-row pessimistic locking to avoid races.
/// </summary>
[WolverineHandler]
[Transactional]
public class ReserveInventoryHandler
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
        ILogger<ReserveInventoryHandler> logger,
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

        // Deterministic sort to avoid deadlocks when locking multiple rows
        var sortedProductIds = command.Items
            .Select(x => x.ProductId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        var stocks = await db.Stocks
            .FromSqlInterpolated($"SELECT s.*, s.xmin FROM inventory.stocks AS s WHERE s.product_id = ANY({sortedProductIds}) ORDER BY s.product_id FOR UPDATE")
            .Include(s => s.Reservations)
            .ToListAsync(ct);

        // CRITICAL FAIL-CLOSED: Verify we locked ALL requested items
        // If we can't lock all, abort to prevent partial reservations during Redis outages
        if (stocks.Count != sortedProductIds.Length)
        {
            var missingIds = sortedProductIds.Except(stocks.Select(s => s.ProductId)).ToList();
            logger.LogError(
                "FAIL-CLOSED: Could not lock all requested products for Order {OrderId}. Missing: {MissingIds}. " +
                "This indicates a critical database consistency issue or missing stock records.",
                command.OrderId,
                string.Join(", ", missingIds));

            return new InventoryReservationFailed(
                command.OrderId,
                "Locking failed: Not all products could be locked for atomic reservation",
                UnavailableProductIds: missingIds);
        }

        foreach (var item in command.Items)
        {
            logger.LogDebug(
                "Processing reservation for Product {ProductId}, Quantity {Quantity}, Order {OrderId}",
                item.ProductId,
                item.Quantity,
                command.OrderId);

            var stock = stocks.FirstOrDefault(s => s.ProductId == item.ProductId);

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
                var reservation = stock.Reserve(command.OrderId, item.Quantity);

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
///     Handler that locks previously reserved inventory to prevent cleanup while payment is processed.
/// </summary>
[WolverineHandler]
[Transactional]
[LocalQueue("inventory-contention")]
public class LockInventoryForPaymentHandler
{
    public static async Task<object> Handle(
        LockInventoryForPaymentCommand command,
        InventoryDbContext db,
        ILogger<LockInventoryForPaymentHandler> logger,
        CancellationToken ct)
    {
        if (command.ReservedItems.Count == 0)
        {
            logger.LogWarning(
                "LockInventoryForPaymentCommand for Order {OrderId} has no reserved items",
                command.OrderId);

            return new InventoryReservationFailed(
                command.OrderId,
                "No reserved items to lock",
                UnavailableProductIds: null);
        }

        var productIds = command.ReservedItems
            .Select(x => x.ProductId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        var reservationIds = command.ReservedItems
            .Select(x => x.ReservationId)
            .Distinct()
            .ToArray();

        var stocks = await db.Stocks
            .FromSqlInterpolated($"SELECT s.*, s.xmin FROM inventory.stocks AS s WHERE s.product_id = ANY({productIds}) ORDER BY s.product_id FOR UPDATE")
            .Include(s => s.Reservations)
            .ToListAsync(ct);

        var missing = new List<Guid>();

        foreach (var item in command.ReservedItems)
        {
            var stock = stocks.FirstOrDefault(s => s.ProductId == item.ProductId);

            if (stock is null)
            {
                logger.LogWarning(
                    "Stock record not found while locking reservation {ReservationId} for Order {OrderId}, Product {ProductId}",
                    item.ReservationId,
                    command.OrderId,
                    item.ProductId);
                missing.Add(item.ProductId);
                continue;
            }

            var reservation = stock.Reservations.FirstOrDefault(r => r.Id == item.ReservationId);
            if (reservation is null)
            {
                logger.LogWarning(
                    "Reservation {ReservationId} not found for Order {OrderId}, Product {ProductId}",
                    item.ReservationId,
                    command.OrderId,
                    item.ProductId);
                missing.Add(item.ProductId);
                continue;
            }

            if (reservation.Status != Domain.Stock.ReservationStatus.Active)
            {
                logger.LogWarning(
                    "Reservation {ReservationId} for Order {OrderId} cannot be locked from status {Status}",
                    reservation.Id,
                    command.OrderId,
                    reservation.Status);
                missing.Add(item.ProductId);
                continue;
            }

            stock.LockReservationForPayment(item.ReservationId);
        }

        if (missing.Count > 0)
        {
            logger.LogWarning(
                "Locking reservations failed for Order {OrderId}. Missing or invalid reservations for {Count} product(s)",
                command.OrderId,
                missing.Count);

            return new InventoryReservationFailed(
                command.OrderId,
                $"Could not lock reservations for {missing.Count} product(s)",
                missing);
        }

        logger.LogInformation(
            "Locked {Count} reservations for Order {OrderId} to proceed with payment",
            command.ReservedItems.Count,
            command.OrderId);

        return new InventoryLocked(command.OrderId, command.ReservedItems);
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
                                         (r.Status == Domain.Stock.ReservationStatus.Active || r.Status == Domain.Stock.ReservationStatus.PendingPayment));

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
