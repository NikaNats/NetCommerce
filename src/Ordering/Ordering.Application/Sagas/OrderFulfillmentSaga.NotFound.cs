using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared.Events;

namespace NetCommerce.Ordering.Application.Sagas;

/// <summary>
///     Late-message handlers of the OrderFulfillmentSaga for completed/purged sagas.
///     Moved verbatim from OrderFulfillmentSaga.cs (NotFound Handlers region, all 12).
///     Wolverine routes messages for unknown saga instances here instead of crashing,
///     which is the normal race when a reply lands after MarkCompleted purged the row.
/// </summary>
public sealed partial class OrderFulfillmentSaga
{
    /// <summary>
    ///     Handles late inventory reservation messages for completed/deleted sagas.
    ///     Prevents crashes when messages arrive after saga is purged.
    /// </summary>
    public static void NotFound(
        InventoryReserved @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryReserved for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryLocked @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryLocked for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryReservationFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryReservationFailed for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        PaymentSucceeded @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late PaymentSucceeded for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        PaymentFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late PaymentFailed for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryConfirmed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryConfirmed for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryConfirmationFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryConfirmationFailed for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryReservationTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryReservationTimeout for Order {OrderId}. Saga already completed, ignoring.",
            timeout.Id);
    }

    public static void NotFound(
        PaymentTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late PaymentTimeout for Order {OrderId}. Saga already completed, ignoring.",
            timeout.Id);
    }

    public static void NotFound(
        InventoryConfirmationTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryConfirmationTimeout for Order {OrderId}. Saga already completed, ignoring.",
            timeout.Id);
    }

    public static void NotFound(
        CompensationStalledTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late CompensationStalledTimeout for Order {OrderId}. Saga already completed, ignoring.",
            timeout.Id);
    }

    public static void NotFound(
        CancelOrderFulfillmentCommand command,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received CancelOrderFulfillmentCommand for Order {OrderId} with no live saga. " +
            "Order was already finalized or failed; nothing to stop.",
            command.OrderId);
    }
}
