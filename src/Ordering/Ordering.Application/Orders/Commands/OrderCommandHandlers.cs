using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Results;
using Wolverine.Attributes;

namespace NetCommerce.Ordering.Application.Orders.Commands;

/// <summary>
///     Wolverine handler for CreateOrderCommand.
///     Uses static method pattern with method injection for testability.
///     Transactional outbox ensures domain events are atomically persisted.
/// </summary>
[WolverineHandler]
public static class CreateOrderHandler
{
    /// <summary>
    ///     Handles order creation and returns the order ID.
    ///     Wolverine auto-wraps this in a transaction via EF Core middleware.
    /// </summary>
    public static async Task<Result<Guid>> HandleAsync(
        CreateOrderCommand command,
        OrderingDbContext db,
        ILogger<CreateOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        var shippingAddress = ShippingAddress.Create(
            command.ShippingAddress.RecipientName,
            command.ShippingAddress.Street,
            command.ShippingAddress.City,
            command.ShippingAddress.State,
            command.ShippingAddress.Country,
            command.ShippingAddress.PostalCode,
            command.ShippingAddress.PhoneNumber);

        var idempotencyKey = $"order-{command.CustomerId}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var order = Order.Create(
            command.CustomerId,
            shippingAddress,
            idempotencyKey);

        var billingAddress = BillingAddress.Create(
            command.BillingAddress.RecipientName,
            command.BillingAddress.Street,
            command.BillingAddress.City,
            command.BillingAddress.State,
            command.BillingAddress.Country,
            command.BillingAddress.PostalCode);
        order.SetBillingAddress(billingAddress);

        foreach (var item in command.Items)
        {
            var money = Money.Create(item.UnitPrice, item.Currency);
            order.AddItem(item.ProductId, item.ProductName, money, item.Quantity);
        }

        db.Orders.Add(order);
        // Wolverine's transactional middleware handles SaveChangesAsync

        logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId}",
            order.Id, command.CustomerId);

        return order.Id;
    }
}

/// <summary>
///     Wolverine handler for CancelOrderCommand.
/// </summary>
[WolverineHandler]
public static class CancelOrderHandler
{
    public static async Task<Result> HandleAsync(
        CancelOrderCommand command,
        OrderingDbContext db,
        ILogger<CancelOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);

        if (order is null)
            return Result.Failure(Error.NotFound("Order", command.OrderId));

        try
        {
            // Grace Period Cancellation Logic:
            // - If order.IsInGracePeriod (Status == Submitted):
            //   * Cancellation is instant and free
            //   * Stock reservation will be released via OrderCancelledIntegrationEvent
            //   * Payment was never taken, so no refund needed
            // - If order is not in grace period:
            //   * May require refund processing
            //   * Compensating transactions may be triggered

            order.Cancel(command.Reason);

            logger.LogInformation(
                "Order {OrderId} cancelled. Reason: {Reason}",
                command.OrderId, command.Reason);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }
    }
}

/// <summary>
///     Wolverine handler for ConfirmOrderCommand (marks order as paid).
/// </summary>
[WolverineHandler]
public static class ConfirmOrderHandler
{
    public static async Task<Result> HandleAsync(
        ConfirmOrderCommand command,
        OrderingDbContext db,
        ILogger<ConfirmOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);

        if (order is null)
            return Result.Failure(Error.NotFound("Order", command.OrderId));

        try
        {
            order.MarkAsPaid(command.PaymentTransactionId);

            logger.LogInformation(
                "Order {OrderId} confirmed with payment {PaymentTransactionId}",
                command.OrderId, command.PaymentTransactionId);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }
    }
}

/// <summary>
///     Wolverine handler for ShipOrderCommand.
/// </summary>
[WolverineHandler]
public static class ShipOrderHandler
{
    public static async Task<Result> HandleAsync(
        ShipOrderCommand command,
        OrderingDbContext db,
        ILogger<ShipOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);

        if (order is null)
            return Result.Failure(Error.NotFound("Order", command.OrderId));

        try
        {
            order.MarkAsShipped(command.TrackingNumber);

            logger.LogInformation(
                "Order {OrderId} shipped with tracking {TrackingNumber}",
                command.OrderId, command.TrackingNumber);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }
    }
}

/// <summary>
///     Wolverine handler for DeliverOrderCommand.
/// </summary>
[WolverineHandler]
public static class DeliverOrderHandler
{
    public static async Task<Result> HandleAsync(
        DeliverOrderCommand command,
        OrderingDbContext db,
        ILogger<DeliverOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);

        if (order is null)
            return Result.Failure(Error.NotFound("Order", command.OrderId));

        try
        {
            order.MarkAsDelivered();

            logger.LogInformation("Order {OrderId} delivered", command.OrderId);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }
    }
}
