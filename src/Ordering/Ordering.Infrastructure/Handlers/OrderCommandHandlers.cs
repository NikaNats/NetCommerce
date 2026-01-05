using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Orders.Services;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
using NetCommerce.SharedKernel.Results;
using Npgsql;
using Wolverine;
using Wolverine.Attributes;

namespace NetCommerce.Ordering.Infrastructure.Handlers;

/// <summary>
///     Wolverine handler for CreateOrderCommand implementing Triple-Pass Pricing Pattern.
///     Pass 1: Fetch RAW price from Catalog (Source of Truth)
///     Pass 2: Apply Promotions & Discounts
///     Pass 3: Calculate Taxes based on Shipping Address
///     Uses static method pattern with method injection for testability.
///     Transactional outbox ensures domain events are atomically persisted.
/// </summary>
[WolverineHandler]
public static class CreateOrderHandler
{
    /// <summary>
    ///     Handles order creation with Triple-Pass Pricing and returns the order ID.
    ///     Wolverine auto-wraps this in a transaction via EF Core middleware.
    ///     Publishes OrderPlacedIntegrationEvent via Outbox for email notifications.
    /// </summary>
    public static async Task<Result<Guid>> HandleAsync(
        CreateOrderCommand command,
        OrderingDbContext db,
        IMessageBus messageBus,
        IPriceLookupService priceLookup,
        IPromotionEngine promotionEngine,
        ITaxProvider taxProvider,
        ILogger<CreateOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Result.Failure<Guid>(Error.Validation("IdempotencyKey is required."));

        var existingOrder = await db.Orders
            .AsNoTracking()
            .Select(o => new { o.Id, o.IdempotencyKey })
            .FirstOrDefaultAsync(o => o.IdempotencyKey == command.IdempotencyKey, cancellationToken);

        if (existingOrder is not null)
        {
            logger.LogWarning(
                "Duplicate order request detected for key {Key}. Returning existing OrderId {OrderId}.",
                command.IdempotencyKey,
                existingOrder.Id);

            return Result.Success(existingOrder.Id);
        }

        var shippingAddress = ShippingAddress.Create(
            command.ShippingAddress.RecipientName,
            command.ShippingAddress.Street,
            command.ShippingAddress.City,
            command.ShippingAddress.State,
            command.ShippingAddress.Country,
            command.ShippingAddress.PostalCode,
            command.ShippingAddress.PhoneNumber);

        // PASS 1: Fetch RAW prices from Catalog (Source of Truth)
        var productIds = command.Items.Select(x => x.ProductId).Distinct();
        var priceMap = await priceLookup.GetPricesAsync(productIds, cancellationToken);

        var order = Order.Create(
            command.CustomerId,
            shippingAddress,
            command.IdempotencyKey);

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
            if (!priceMap.TryGetValue(item.ProductId, out var catalogMeta))
                return Result.Failure<Guid>(Error.NotFound("Product", item.ProductId));

            var basePrice = catalogMeta.Price.Amount;

            // Server-side price guard: Detect price changes between cart and checkout
            if (item.ExpectedPrice.HasValue && basePrice != item.ExpectedPrice.Value)
            {
                logger.LogWarning(
                    "Price guard triggered for product {ProductId}: expected {Expected}, actual {Actual}",
                    item.ProductId,
                    item.ExpectedPrice.Value,
                    basePrice);

                return Result.Failure<Guid>(Error.Conflict(
                    $"Price for {catalogMeta.Name} has changed. Expected {item.ExpectedPrice.Value:C}, but current price is {basePrice:C}. Please review your cart."));
            }

            // PASS 2: Apply Promotions & Discounts
            var promotionResult = await promotionEngine.CalculateDiscountAsync(
                item.ProductId,
                basePrice,
                item.Quantity,
                command.CustomerId,
                command.CouponCode,
                cancellationToken);

            var subTotal = (basePrice * item.Quantity) - promotionResult.DiscountAmount;

            // PASS 3: Calculate Taxes based on Shipping Address
            var taxResult = await taxProvider.GetTaxAsync(
                subTotal,
                command.ShippingAddress.Country,
                catalogMeta.Category,
                cancellationToken);

            // 2025 Elite Refinement: Store LINE TOTALS to avoid penny variance from division
            // promotionResult.DiscountAmount and taxResult.Amount are ALREADY line totals
            var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
                basePrice,
                item.Quantity,
                lineDiscountTotal: promotionResult.DiscountAmount,  // Store line total directly
                lineTaxTotal: taxResult.Amount,                      // Store line total directly
                taxResult.Rate,
                taxResult.Type,
                catalogMeta.Price.Currency);

            // Calculate final unit price
            var finalUnitPrice = Money.Create(priceBreakdown.FinalPrice, catalogMeta.Price.Currency);

            logger.LogInformation(
                "Pricing calculated for {Product}: Base={Base}, Discount={Discount}, Tax={Tax}, Final={Final}",
                catalogMeta.Name,
                priceBreakdown.BasePrice,
                priceBreakdown.DiscountAmount,
                priceBreakdown.TaxAmount,
                priceBreakdown.FinalPrice);

            // Add item with complete pricing breakdown
            order.AddItem(
                item.ProductId,
                catalogMeta.Name,
                finalUnitPrice,
                item.Quantity,
                catalogMeta.WeightKg,
                priceBreakdown,
                catalogMeta.Sku);
        }

        try
        {
            db.Orders.Add(order);
            // Publish OrderPlacedIntegrationEvent via Wolverine Outbox
            // This ensures the email is only sent if the order transaction commits successfully
            await messageBus.PublishAsync(new OrderPlacedIntegrationEvent(
                order.Id,
                order.OrderNumber,
                command.CustomerEmail,
                command.CustomerName,
                order.TotalAmount));

            // Wolverine's transactional middleware handles SaveChangesAsync

            logger.LogInformation(
                "Order {OrderId} created for customer {CustomerId} with total {Total}. Idempotency key: {Key}",
                order.Id, command.CustomerId, order.TotalAmount, command.IdempotencyKey);

            return order.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            var duplicate = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.IdempotencyKey == command.IdempotencyKey, cancellationToken);

            logger.LogWarning(
                "Unique constraint hit for idempotency key {Key}. Returning existing OrderId {OrderId}.",
                command.IdempotencyKey,
                duplicate.Id);

            return Result.Success(duplicate.Id);
        }
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
