using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Application.EventHandlers;

/// <summary>
///     Handlers for Saga commands in the Inventory module.
///     These handlers process inventory operations from the OrderFulfillmentSaga.
/// </summary>
[WolverineHandler]
public static class SagaInventoryHandlers
{
    /// <summary>
    ///     Handles inventory reservation request from the OrderFulfillmentSaga.
    ///     Performs a "soft" reservation - stock is reserved but not deducted.
    /// </summary>
    public static async Task<object> Handle(
        ReserveInventoryCommand command,
        ILogger<ReserveInventoryCommand> logger)
    {
        logger.LogInformation(
            "Reserving inventory for Order {OrderId}. Items: {ItemCount}",
            command.OrderId,
            command.Items.Count);

        try
        {
            // Simulate inventory check and reservation
            // In production: Use pessimistic locking (FOR UPDATE) to prevent overselling
            await Task.Delay(50);

            var reservedItems = new List<ReservedItem>();

            foreach (var item in command.Items)
            {
                // Simulate checking stock availability
                // In production: Query database with row-level locking

                // Create reservation
                var reservationId = Guid.NewGuid();
                reservedItems.Add(new ReservedItem(
                    item.ProductId,
                    reservationId,
                    item.Quantity));

                logger.LogDebug(
                    "Reserved {Quantity} units of Product {ProductId} for Order {OrderId}. ReservationId: {ReservationId}",
                    item.Quantity,
                    item.ProductId,
                    command.OrderId,
                    reservationId);
            }

            logger.LogInformation(
                "Inventory reservation successful for Order {OrderId}. Reserved {Count} items.",
                command.OrderId,
                reservedItems.Count);

            return new InventoryReserved(command.OrderId, reservedItems);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Inventory reservation failed for Order {OrderId}. Error: {Error}",
                command.OrderId,
                ex.Message);

            return new InventoryReservationFailed(
                command.OrderId,
                ex.Message,
                UnavailableProductIds: null);
        }
    }

    /// <summary>
    ///     Handles inventory confirmation request from the OrderFulfillmentSaga.
    ///     Converts soft reservation to hard deduction after payment is confirmed.
    /// </summary>
    public static async Task<object> Handle(
        ConfirmInventoryCommand command,
        ILogger<ConfirmInventoryCommand> logger)
    {
        logger.LogInformation(
            "Confirming inventory for Order {OrderId}. PaymentTransactionId: {TransactionId}",
            command.OrderId,
            command.PaymentTransactionId);

        try
        {
            // Simulate converting reservation to actual deduction
            // In production: Update stock records to deduct reserved quantities
            await Task.Delay(50);

            logger.LogInformation(
                "Inventory confirmed for Order {OrderId}. Stock has been deducted.",
                command.OrderId);

            return new InventoryConfirmed(command.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Inventory confirmation failed for Order {OrderId}. Error: {Error}",
                command.OrderId,
                ex.Message);

            return new InventoryConfirmationFailed(command.OrderId, ex.Message);
        }
    }

    /// <summary>
    ///     Handles inventory release request from the OrderFulfillmentSaga.
    ///     This is a compensating action - releases reserved stock back to available pool.
    /// </summary>
    public static async Task Handle(
        ReleaseInventoryReservationCommand command,
        ILogger<ReleaseInventoryReservationCommand> logger)
    {
        logger.LogWarning(
            "Releasing inventory reservation for Order {OrderId}. Reason: {Reason}",
            command.OrderId,
            command.Reason);

        try
        {
            // Simulate releasing reservations
            // In production: Delete reservation records or mark as released
            await Task.Delay(50);

            logger.LogInformation(
                "Inventory reservation released for Order {OrderId}.",
                command.OrderId);
        }
        catch (Exception ex)
        {
            // Log but don't throw - this is a compensating action
            // Manual intervention may be needed
            logger.LogCritical(ex,
                "CRITICAL: Failed to release inventory reservation for Order {OrderId}. " +
                "Manual intervention required to prevent stock discrepancy!",
                command.OrderId);
        }
    }
}
