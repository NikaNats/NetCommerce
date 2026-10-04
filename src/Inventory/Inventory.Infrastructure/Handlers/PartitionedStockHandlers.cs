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
    ///     Thread Safety: comes from <c>SELECT ... FOR UPDATE</c> row locks in
    ///     deterministic product-id order inside an explicit transaction (below),
    ///     NOT from Wolverine partitioning. Saga identity is the OrderId, and this
    ///     handler sits on a shared local queue — two orders for the same product
    ///     routinely execute concurrently. The locks (plus the fail-closed
    ///     all-rows-locked check) are what prevent oversell; do not remove them
    ///     on the assumption that partitioning serializes per product.
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

        // Execution-strategy transaction (NOT Wolverine Eager mode, which is
        // incompatible with EnableRetryOnFailure): the SELECT ... FOR UPDATE
        // below only holds row locks inside an explicit transaction. Without
        // one (autocommit), concurrent reservers for the same product collide
        // on xmin optimistic-concurrency checks instead of serializing.
        // The strategy additionally retries the whole unit on transient faults.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await ReserveAsync(command, db, logger, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static async Task<object> ReserveAsync(
        ReserveInventoryCommand command,
        InventoryDbContext db,
        ILogger<ReserveInventoryHandler> logger,
        CancellationToken ct)
    {
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

            // ── Pass 1: Validate all items have sufficient stock ──────────────
            // Validation runs before any mutation, and the explicit transaction
            // above commits only on full success — failed validations return
            // without persisting anything (true all-or-nothing semantics).
            foreach (var item in command.Items)
            {
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

                if (stock.GetAvailableQuantity() < item.Quantity)
                {
                    logger.LogWarning(
                        "Insufficient stock for Product {ProductId}, Order {OrderId}: " +
                        "Requested {Requested}, Available {Available}",
                        item.ProductId,
                        command.OrderId,
                        item.Quantity,
                        stock.GetAvailableQuantity());

                    unavailableProducts.Add(item.ProductId);
                }
            }

            // Fail early — no entities were modified, nothing to roll back
            if (unavailableProducts.Count > 0)
            {
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

            // ── Pass 2: Reserve all items (all validated as available) ────────
            foreach (var item in command.Items)
            {
                var stock = stocks.First(s => s.ProductId == item.ProductId);

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

        // Same execution-strategy transaction rationale as ReserveAsync above:
        // SELECT ... FOR UPDATE only serializes inside an explicit transaction.
        // Commit happens solely on full success; partial locks are rolled back
        // rather than persisted (the previous middleware-committed behavior
        // could leave half-locked reservations behind on the failure path).
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await LockAsync(command, db, logger, ct);

                // Commit only when every reservation locked; otherwise roll back
                // the partial work so no half-locked state survives.
                if (result is InventoryLocked)
                {
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                else
                {
                    await transaction.RollbackAsync(ct);
                }

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static async Task<object> LockAsync(
        LockInventoryForPaymentCommand command,
        InventoryDbContext db,
        ILogger<LockInventoryForPaymentHandler> logger,
        CancellationToken ct)
    {

            var productIds = command.ReservedItems
                .Select(x => x.ProductId)
                .Distinct()
                .OrderBy(id => id)
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
/// <remarks>
/// DDIA atomicity scope: the read (which reservations are confirmable) and the
/// write (deduction) run inside ONE explicit transaction under
/// <c>SELECT ... FOR UPDATE</c> row locks, mirroring
/// <see cref="ReserveInventoryHandler"/>. Without this, the reservation cleanup
/// job can release a reservation between this handler's read and its
/// SaveChanges, losing the deduction or colliding on the xmin row version.
/// Replay contract: a redelivery after a successful confirm finds the rows
/// but no eligible reservations (all terminal) and still reports
/// <c>InventoryConfirmed</c> — failing here would wrongly compensate an
/// already-paid order. No rows at all means nothing was ever reserved and
/// correctly reports <c>InventoryConfirmationFailed</c>.
/// </remarks>
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

        // Same execution-strategy transaction as ReserveInventoryHandler: row
        // locks only serialize inside an explicit transaction (autocommit would
        // release them immediately), and the strategy retries transient faults.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await ConfirmAsync(command, db, logger, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static async Task<object> ConfirmAsync(
        ConfirmInventoryCommand command,
        InventoryDbContext db,
        ILogger<PartitionedConfirmInventoryHandler> logger,
        CancellationToken ct)
    {
        // Lock the affected stock rows BEFORE reading reservation state, in
        // deterministic order, so concurrent cleanup/release serializes here
        // instead of racing the confirm.
        var productIds = await db.Stocks
            .Where(s => s.Reservations.Any(r => r.OrderId == command.OrderId))
            .Select(s => s.ProductId)
            .OrderBy(id => id)
            .ToArrayAsync(ct);

        if (productIds.Length == 0)
        {
            logger.LogWarning(
                "No reservations found for Order {OrderId}",
                command.OrderId);

            return new InventoryConfirmationFailed(
                command.OrderId,
                "No reservations found for this order");
        }

        var stocks = await db.Stocks
            .FromSqlInterpolated($"SELECT s.*, s.xmin FROM inventory.stocks AS s WHERE s.product_id = ANY({productIds}) ORDER BY s.product_id FOR UPDATE")
            .Include(s => s.Reservations)
            .ToListAsync(ct);

        // Declared outside try: the catch path below must know whether a
        // mutation already applied before deciding rollback-vs-failure-event.
        var confirmedCount = 0;

        try
        {
            // Two-pass within the lock: collect eligible reservations first, then
            // mutate. The loop body has no throw source beyond the status guard
            // (already filtered), but if a future domain rule throws mid-loop we
            // must not persist a partial deduction alongside a failure event.
            var eligible = new List<(Domain.Stock.Stock Stock, Guid ReservationId)>();
            foreach (var stock in stocks)
            {
                var reservation = stock.Reservations
                    .FirstOrDefault(r => r.OrderId == command.OrderId &&
                                         (r.Status == Domain.Stock.ReservationStatus.Active || r.Status == Domain.Stock.ReservationStatus.PendingPayment));

                if (reservation is not null)
                    eligible.Add((stock, reservation.Id));
            }

            foreach (var (stock, reservationId) in eligible)
            {
                stock.ConfirmReservation(reservationId);
                confirmedCount++;

                logger.LogDebug(
                    "Confirmed reservation {ReservationId} for Product {ProductId}, Order {OrderId}",
                    reservationId,
                    stock.ProductId,
                    command.OrderId);
            }

            if (confirmedCount == 0)
            {
                // Idempotent replay: the rows exist but every reservation for
                // this order already reached a terminal state (a previous
                // confirm) or left Active/PendingPayment via another path.
                // Report success — failing here would compensate a paid order.
                logger.LogInformation(
                    "Inventory confirm replay for Order {OrderId}: {StockCount} stock row(s) locked, " +
                    "no eligible reservations. Returning confirmed (already settled).",
                    command.OrderId,
                    stocks.Count);
            }
            else
            {
                logger.LogInformation(
                    "Inventory confirmed for Order {OrderId}. Confirmed {Count} reservations. " +
                    "Stock has been permanently deducted.",
                    command.OrderId,
                    confirmedCount);
            }

            return new InventoryConfirmed(command.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Inventory confirmation failed for Order {OrderId}: {Error}",
                command.OrderId,
                ex.Message);

            if (confirmedCount > 0)
            {
                // A mutation already applied: returning a failure event here
                // would persist a PARTIAL deduction (caller commits after this
                // return). Rethrow so the transaction rolls back and Wolverine
                // redelivers; the replay converges via the idempotent path.
                throw;
            }

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
