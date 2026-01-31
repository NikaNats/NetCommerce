using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Orders.Services;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Kernel.Core.Results;
using Npgsql;
using Wolverine;
using Wolverine.Attributes;

namespace NetCommerce.Ordering.Infrastructure.Handlers;

/// <summary>
///     Context data loaded during the LoadAsync phase of the compound handler.
///     Contains all pre-fetched data needed for order creation business logic.
/// </summary>
public sealed record CreateOrderContext(
    Dictionary<Guid, PriceSnapshot> PriceMap,
    Dictionary<Guid, PromotionResult> PromotionResults,
    Dictionary<Guid, TaxCalculationResult> TaxResults,
    Guid? ExistingOrderId);

/// <summary>
///     Wolverine Compound Handler for CreateOrderCommand implementing Triple-Pass Pricing Pattern.
///
///     This handler is split into two phases per Wolverine best practices:
///     1. LoadAsync - Infrastructure concerns: DB queries, external service calls
///     2. Handle - Pure business logic: Validation, domain object creation, event publishing
///
///     Benefits:
///     - LoadAsync is mockable for unit tests (just return test data)
///     - Handle is a pure function - deterministic, easy to test
///     - Clear separation of I/O from business logic (A-Frame Architecture)
/// </summary>
[WolverineHandler]
public static class CreateOrderHandler
{
    /// <summary>
    ///     PHASE 1: Load all required data from infrastructure.
    ///     This method handles all async I/O operations:
    ///     - Idempotency check
    ///     - Price lookup from Catalog
    ///     - Promotion calculations
    ///     - Tax calculations
    /// </summary>
    public static async Task<CreateOrderContext> LoadAsync(
        CreateOrderCommand command,
        OrderingDbContext db,
        IPriceLookupService priceLookup,
        IPromotionEngine promotionEngine,
        ITaxProvider taxProvider,
        ILogger<CreateOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        // Idempotency check
        var existingOrder = await db.Orders
            .AsNoTracking()
            .Select(o => new { o.Id, o.IdempotencyKey })
            .FirstOrDefaultAsync(o => o.IdempotencyKey == command.IdempotencyKey, cancellationToken);

        if (existingOrder is not null)
        {
            logger.LogWarning(
                "Duplicate order request detected for key {Key}. Will return existing OrderId {OrderId}.",
                command.IdempotencyKey,
                existingOrder.Id);

            return new CreateOrderContext([], [], [], existingOrder.Id);
        }

        // PASS 1: Fetch RAW prices from Catalog (Source of Truth)
        var productIds = command.Items.Select(x => x.ProductId).Distinct();
        var priceMap = await priceLookup.GetPricesAsync(productIds, cancellationToken);

        // PASS 2 & 3: Pre-calculate promotions and taxes for each item
        var promotionResults = new Dictionary<Guid, PromotionResult>();
        var taxResults = new Dictionary<Guid, TaxCalculationResult>();

        foreach (var item in command.Items)
        {
            if (!priceMap.TryGetValue(item.ProductId, out var catalogMeta))
                continue; // Will be caught in Handle phase

            var basePrice = catalogMeta.Price.Amount;

            // PASS 2: Apply Promotions & Discounts
            var promotionResult = await promotionEngine.CalculateDiscountAsync(
                item.ProductId,
                basePrice,
                item.Quantity,
                command.CustomerId,
                command.CouponCode,
                cancellationToken);
            promotionResults[item.ProductId] = promotionResult;

            var subTotal = (basePrice * item.Quantity) - promotionResult.DiscountAmount;

            // PASS 3: Calculate Taxes based on Shipping Address
            var taxResult = await taxProvider.GetTaxAsync(
                subTotal,
                command.ShippingAddress.Country,
                catalogMeta.Category,
                cancellationToken);
            taxResults[item.ProductId] = taxResult;
        }

        return new CreateOrderContext(priceMap, promotionResults, taxResults, null);
    }

    /// <summary>
    ///     PHASE 2: Pure business logic - no async, no I/O.
    ///     This method is deterministic and easily unit testable.
    ///     Returns a tuple of (Result, CascadingMessage) per Wolverine conventions.
    /// </summary>
    public static async Task<Result<Guid>> Handle(
        CreateOrderCommand command,
        CreateOrderContext context,
        OrderingDbContext db,
        IMessageBus messageBus,
        ILogger<CreateOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        // Handle idempotency - return existing order if already created
        if (context.ExistingOrderId.HasValue)
            return Result.Success(context.ExistingOrderId.Value);

        // Validate idempotency key
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Result.Failure<Guid>(Error.Validation("IdempotencyKey is required."));

        var shippingAddress = ShippingAddress.Create(
            command.ShippingAddress.RecipientName,
            command.ShippingAddress.Street,
            command.ShippingAddress.City,
            command.ShippingAddress.State,
            command.ShippingAddress.Country,
            command.ShippingAddress.PostalCode,
            command.ShippingAddress.PhoneNumber);

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
            if (!context.PriceMap.TryGetValue(item.ProductId, out var catalogMeta))
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

            var promotionResult = context.PromotionResults[item.ProductId];
            var taxResult = context.TaxResults[item.ProductId];

            // 2025 Elite Refinement: Store LINE TOTALS to avoid penny variance from division
            var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
                basePrice,
                item.Quantity,
                lineDiscountTotal: promotionResult.DiscountAmount,
                lineTaxTotal: taxResult.Amount,
                taxResult.Rate,
                taxResult.Type,
                catalogMeta.Price.Currency);

            var finalUnitPrice = Money.Create(priceBreakdown.FinalPrice, catalogMeta.Price.Currency);

            logger.LogInformation(
                "Pricing calculated for {Product}: Base={Base}, Discount={Discount}, Tax={Tax}, Final={Final}",
                catalogMeta.Name,
                priceBreakdown.BasePrice,
                priceBreakdown.DiscountAmount,
                priceBreakdown.TaxAmount,
                priceBreakdown.FinalPrice);

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
            await messageBus.PublishAsync(new OrderPlacedIntegrationEvent(
                order.Id,
                order.OrderNumber,
                command.CustomerEmail,
                command.CustomerName,
                order.TotalAmount));

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
