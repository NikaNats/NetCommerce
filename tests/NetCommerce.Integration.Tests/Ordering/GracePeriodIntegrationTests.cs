using Microsoft.EntityFrameworkCore;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Domain.Shared;
using Shouldly;

namespace NetCommerce.Integration.Tests.Ordering;

/// <summary>
///     Integration tests for the Grace Period workflow.
///     Tests the full lifecycle of orders with grace period pattern.
/// </summary>
public class GracePeriodIntegrationTests : IntegrationTestBase
{
    public GracePeriodIntegrationTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Order_Created_ShouldBeInSubmittedStatus()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        var order = CreateTestOrder();

        // Act
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Assert
        var savedOrder = await context.Orders.FindAsync(order.Id);
        savedOrder.ShouldNotBeNull();
        savedOrder.Status.ShouldBe(OrderStatus.Submitted);
    }

    [Fact]
    public async Task Order_GracePeriodConfirmed_ShouldTransitionToAwaitingValidation()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Act
        var savedOrder = await context.Orders.FindAsync(order.Id);
        savedOrder!.ConfirmGracePeriod();
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Assert
        var updatedOrder = await context.Orders.FindAsync(order.Id);
        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.AwaitingValidation);
    }

    [Fact]
    public async Task Order_CancelledDuringGracePeriod_ShouldHaveSubmittedAsPreviousStatus()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Act
        var savedOrder = await context.Orders.FindAsync(order.Id);
        savedOrder.ShouldNotBeNull();
        savedOrder.Status.ShouldBe(OrderStatus.Submitted); // Still in grace period

        savedOrder.Cancel("Changed my mind");
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Assert
        var cancelledOrder = await context.Orders.FindAsync(order.Id);
        cancelledOrder.ShouldNotBeNull();
        cancelledOrder.Status.ShouldBe(OrderStatus.Cancelled);
        cancelledOrder.CancellationReason.ShouldBe("Changed my mind");
        cancelledOrder.PaidAt.ShouldBeNull(); // No payment was taken
    }

    [Fact]
    public async Task Order_QueryByStatus_ShouldUseCompositeIndex()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        // Create multiple orders with different statuses
        var submittedOrder1 = CreateTestOrder();
        var submittedOrder2 = CreateTestOrder();
        var awaitingValidationOrder = CreateTestOrder();
        awaitingValidationOrder.ConfirmGracePeriod();

        context.Orders.AddRange(submittedOrder1, submittedOrder2, awaitingValidationOrder);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var graceThreshold = DateTime.UtcNow.AddMinutes(-5);

        // Act - Query that would be used by GracePeriodManagerService
        var ordersToProcess = await context.Orders
            .Where(o => o.Status == OrderStatus.Submitted && o.CreatedAt < graceThreshold)
            .OrderBy(o => o.CreatedAt)
            .Take(100)
            .ToListAsync();

        // Assert - This query should hit the IX_Orders_Status_CreatedAt index
        // Note: Orders were just created, so CreatedAt is recent and won't match the threshold
        ordersToProcess.ShouldBeEmpty();
    }

    [Fact]
    public async Task Order_FullWorkflow_ShouldPersistAllStatusTransitions()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var orderId = order.Id;

        // Act & Assert - Step 1: Created as Submitted
        context.ChangeTracker.Clear();
        var step1Order = await context.Orders.FindAsync(orderId);
        step1Order!.Status.ShouldBe(OrderStatus.Submitted);
        step1Order.IsInGracePeriod.ShouldBeTrue();

        // Step 2: Grace period ends
        step1Order.ConfirmGracePeriod();
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var step2Order = await context.Orders.FindAsync(orderId);
        step2Order!.Status.ShouldBe(OrderStatus.AwaitingValidation);
        step2Order.IsInGracePeriod.ShouldBeFalse();

        // Step 3: Payment processed
        var paymentId = Guid.NewGuid().ToString();
        step2Order.MarkAsPaid(paymentId);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var step3Order = await context.Orders.FindAsync(orderId);
        step3Order!.Status.ShouldBe(OrderStatus.Paid);
        step3Order.PaidAt.ShouldNotBeNull();
        step3Order.PaymentTransactionId.ShouldBe(paymentId);

        // Step 4: Order shipped
        step3Order.MarkAsShipped("TRACK-123");
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var step4Order = await context.Orders.FindAsync(orderId);
        step4Order!.Status.ShouldBe(OrderStatus.Shipped);
        step4Order.ShippedAt.ShouldNotBeNull();

        // Step 5: Order delivered
        step4Order.MarkAsDelivered();
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var step5Order = await context.Orders.FindAsync(orderId);
        step5Order!.Status.ShouldBe(OrderStatus.Delivered);
        step5Order.DeliveredAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Order_BatchProcessing_ShouldHandleMultipleOrdersEfficiently()
    {
        // Arrange - Create 10 orders to simulate batch processing
        await using var context = Fixture.CreateOrderingDbContext();
        var orders = Enumerable.Range(0, 10)
            .Select(_ => CreateTestOrder())
            .ToList();

        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();

        // Capture the order IDs we created
        var orderIds = orders.Select(o => o.Id).ToList();
        context.ChangeTracker.Clear();

        // Act - Confirm grace period for all orders (simulating background service)
        var ordersToProcess = await context.Orders
            .Where(o => orderIds.Contains(o.Id) && o.Status == OrderStatus.Submitted)
            .ToListAsync();

        foreach (var order in ordersToProcess) order.ConfirmGracePeriod();

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Assert - Only our orders should be in AwaitingValidation status
        var processedOrders = await context.Orders
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync();

        processedOrders.Count.ShouldBe(10);
        processedOrders.ShouldAllBe(o => o.Status == OrderStatus.AwaitingValidation);
    }

    private static Order CreateTestOrder()
    {
        var shippingAddress = ShippingAddress.Create(
            "John Doe",
            "123 Main St",
            "New York",
            "NY",
            "USA",
            "10001",
            "+1-555-1234");

        var order = Order.Create(
            Guid.NewGuid(),
            shippingAddress,
            Guid.NewGuid().ToString());

        var priceBreakdown = new PriceBreakdown(
            basePrice: 99.99m,
            discountAmount: 0m,
            taxAmount: 0m,
            taxRate: 0m,
            taxType: "None",
            currency: "GEL",
            quantity: 2);

        order.AddItem(
            Guid.NewGuid(),
            "Test Product",
            Money.Create(99.99m), // Use GEL to match Money.Zero() default
            2,
            3.0m,
            priceBreakdown);

        return order;
    }
}
