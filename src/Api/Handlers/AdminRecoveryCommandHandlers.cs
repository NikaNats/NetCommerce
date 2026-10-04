#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Api.Endpoints.Admin;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace NetCommerce.Api.Handlers;

/// <summary>
///     Wolverine handlers for the admin order-recovery commands published by
///     <see cref="AdminOrderRecoveryEndpoints"/>. Without these, every admin
///     recovery endpoint publishes a message that no handler subscribes to.
/// </summary>
[WolverineHandler]
public static class AdminRecoveryCommandHandlers
{
    [Transactional]
    public static async Task Handle(
        ForceCompleteOrderSagaCommand command,
        OrderingDbContext db,
        IMessageBus bus,
        ILogger<ForceCompleteOrderSagaCommand> logger,
        CancellationToken ct)
    {
        logger.LogWarning(
            "Admin {User} force-completing Order {OrderId}: {Reason}",
            command.ProcessedByUserId, command.OrderId, command.Reason);

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, ct);
        if (order is not null)
        {
            AdvanceOrderToDelivered(order, logger, command.OrderId);
        }

        // Saga state lives in Wolverine's own store table, not in the EF model
        // (see WolverineSagaStateReader): delete the saga row directly. Late
        // messages for it land in the saga's NotFound handlers by design.
        await WolverineSagaStateReader.DeleteSagaAsync(db, command.OrderId, ct);

        await bus.PublishAsync(
            new OrderStatusChanged(command.OrderId, "Success", "Order marked complete by administrator."));
    }

    [Transactional]
    public static async Task Handle(
        OverridePaymentStatusCommand command,
        OrderingDbContext db,
        IMessageBus bus,
        ILogger<OverridePaymentStatusCommand> logger,
        CancellationToken ct)
    {
        logger.LogWarning(
            "Admin {User} overriding payment for Order {OrderId} to {Status}",
            command.ProcessedByUserId, command.OrderId, command.PaymentStatus);

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, ct);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found for payment override.", command.OrderId);
            return;
        }

        if (string.Equals(command.PaymentStatus, "Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            AdvanceOrderToPaid(order, command.StripeChargeId ?? "MANUAL_OVERRIDE", logger);
            await bus.PublishAsync(
                new PaymentSucceeded(
                    command.OrderId,
                    command.StripeChargeId ?? "OVERRIDE",
                    order.TotalAmount));
        }
        else
        {
            TryCancel(order, $"Payment overridden to failed by admin: {command.Reason}", logger);
            await bus.PublishAsync(
                new PaymentFailed(command.OrderId, command.Reason, "ADMIN_OVERRIDE"));
        }
    }

    [Transactional]
    public static async Task Handle(
        ForceCancelOrderCommand command,
        OrderingDbContext db,
        IMessageBus bus,
        ILogger<ForceCancelOrderCommand> logger,
        CancellationToken ct)
    {
        logger.LogWarning(
            "Admin {User} force-cancelling Order {OrderId}",
            command.ProcessedByUserId, command.OrderId);

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, ct);
        if (order is not null && order.Status != OrderStatus.Cancelled)
        {
            TryCancel(order, command.Reason, logger);
        }

        // Route release + refund decisions through the saga: it knows whether
        // inventory is actually held and whether payment was captured. Publishing
        // RefundPaymentCommand directly here would double-refund whenever the
        // saga also compensates. Saga-path refunds now carry a deterministic
        // PSP idempotency key (IdempotencyKeys.ForOrderRefund), but two
        // DIFFERENT intents for one order (admin action + saga compensation)
        // carry different keys by design, so the saga must stay the single
        // refund decision-maker.
        // The release is ALSO sent directly as belt-and-braces: if the saga is
        // already gone, its NotFound path does nothing and stock would leak
        // until the reservation cleanup job. The release handler is a no-op
        // when no active reservation exists, so duplicates are harmless.
        await bus.PublishAsync(
            new CancelOrderFulfillmentCommand(
                command.OrderId,
                $"Admin force cancel: {command.Reason}"));

        await bus.PublishAsync(
            new ReleaseInventoryReservationCommand(
                command.OrderId,
                $"Admin force cancel: {command.Reason}"));
    }

    public static async Task Handle(
        RetrySagaStepCommand command,
        IMessageBus bus,
        ILogger<RetrySagaStepCommand> logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Retrying step {Step} for Order {OrderId}", command.Step, command.OrderId);

        object retryMessage = command.Step switch
        {
            nameof(OrderFulfillmentState.ReservingInventory) =>
                new InventoryReservationTimeoutMessage { Id = command.OrderId },
            nameof(OrderFulfillmentState.ProcessingPayment) =>
                new PaymentTimeoutMessage { Id = command.OrderId },
            nameof(OrderFulfillmentState.ConfirmingInventory) =>
                new InventoryConfirmationTimeoutMessage { Id = command.OrderId },
            nameof(OrderFulfillmentState.InGracePeriod) or nameof(OrderFulfillmentState.LockingInventory) =>
                new GracePeriodTimeout { Id = command.OrderId },
            _ => new InventoryReservationTimeoutMessage { Id = command.OrderId }
        };

        await bus.PublishAsync(retryMessage);
    }

    public static async Task Handle(
        BulkRetrySagasCommand command,
        OrderingDbContext db,
        IMessageBus bus,
        ILogger<BulkRetrySagasCommand> logger,
        CancellationToken ct)
    {
        logger.LogWarning("Executing bulk retry for state {State}", command.SagaState);

        if (!Enum.TryParse<OrderFulfillmentState>(command.SagaState, out var targetState))
        {
            logger.LogWarning("Unknown saga state '{State}' in bulk retry request.", command.SagaState);
            return;
        }

        var stuckIds = (await WolverineSagaStateReader.QuerySagasAsync(
                db,
                targetState,
                command.MaxOrdersToRetry,
                ct))
            .OrderBy(s => s.StartedAt)
            .Select(s => s.OrderId)
            .ToList();

        foreach (var orderId in stuckIds)
        {
            await bus.PublishAsync(
                new RetrySagaStepCommand(orderId, command.SagaState, command.ProcessedByUserId));
        }
    }

    private static void AdvanceOrderToPaid(
        Order order,
        string transactionId,
        ILogger logger)
    {
        try
        {
            if (order.Status == OrderStatus.AwaitingValidation)
                order.ConfirmStock();
            if (order.Status == OrderStatus.StockConfirmed)
                order.MarkAsPaid(transactionId);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                "Could not transition order {OrderId} to Paid: {Error}. Current status: {Status}",
                order.Id, ex.Message, order.Status);
        }
    }

    private static void AdvanceOrderToDelivered(
        Order order,
        ILogger logger,
        Guid orderId)
    {
        try
        {
            if (order.Status == OrderStatus.AwaitingValidation)
                order.ConfirmStock();
            if (order.Status == OrderStatus.StockConfirmed)
                order.MarkAsPaid("MANUAL_ADMIN_OVERRIDE");
            if (order.Status == OrderStatus.Paid)
                order.MarkAsShipped("MANUAL_ADMIN_OVERRIDE");
            if (order.Status == OrderStatus.Shipped)
                order.MarkAsDelivered();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                "Could not force-complete order {OrderId}: {Error}. Current status: {Status}",
                orderId, ex.Message, order.Status);
        }
    }

    private static void TryCancel(Order order, string reason, ILogger logger)
    {
        try
        {
            if (order.Status != OrderStatus.Cancelled)
                order.Cancel(reason);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                "Could not cancel order {OrderId}: {Error}. Current status: {Status}",
                order.Id, ex.Message, order.Status);
        }
    }
}
