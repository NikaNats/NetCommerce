using NetCommerce.Domain.Tests.Fakers;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Domain;
using Shouldly;

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Unit tests for Order aggregate.
/// </summary>
public class OrderTests
{
    #region Order Workflow - Full Lifecycle Test

    [Fact]
    public void Order_FullWorkflow_ShouldProgressThroughAllStatuses()
    {
        // Arrange & Act
        var order = Order.Create(
            Guid.NewGuid(),
            ShippingAddressFaker.Generate(),
            Guid.NewGuid().ToString());

        order.AddItem(Guid.NewGuid(), "PS5", Money.Create(499.99m), 1);
        order.Status.ShouldBe(OrderStatus.Pending);

        order.MarkAsPaid(Guid.NewGuid());
        order.Status.ShouldBe(OrderStatus.Paid);

        order.StartProcessing();
        order.Status.ShouldBe(OrderStatus.Processing);

        order.MarkAsShipped("TRACK-001");
        order.Status.ShouldBe(OrderStatus.Shipped);

        order.MarkAsDelivered();
        order.Status.ShouldBe(OrderStatus.Delivered);

        // Assert - all timestamps should be set
        order.CreatedAt.ShouldNotBe(default);
        order.PaidAt.ShouldNotBeNull();
        order.ShippedAt.ShouldNotBeNull();
        order.DeliveredAt.ShouldNotBeNull();
        order.CancelledAt.ShouldBeNull();
    }

    #endregion

    #region Create Tests

    [Fact]
    public void Create_WithValidData_ShouldCreateOrder()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var shippingAddress = ShippingAddressFaker.Generate();
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act
        var order = Order.Create(customerId, shippingAddress, idempotencyKey);

        // Assert
        order.ShouldNotBeNull();
        order.Id.ShouldNotBe(Guid.Empty);
        order.CustomerId.ShouldBe(customerId);
        order.ShippingAddress.ShouldBe(shippingAddress);
        order.IdempotencyKey.ShouldBe(idempotencyKey);
        order.Status.ShouldBe(OrderStatus.Pending);
        order.OrderNumber.ShouldStartWith("ORD-");
        order.TotalAmount.Amount.ShouldBe(0);
    }

    [Fact]
    public void Create_ShouldRaise_OrderCreatedDomainEvent()
    {
        // Act
        var order = OrderFaker.Generate();

        // Assert
        order.DomainEvents.ShouldContain(e => e is OrderCreatedDomainEvent);

        var createdEvent = order.DomainEvents.OfType<OrderCreatedDomainEvent>().Single();
        createdEvent.OrderId.ShouldBe(order.Id);
        createdEvent.OrderNumber.ShouldBe(order.OrderNumber);
    }

    #endregion

    #region AddItem Tests (Price Snapshotting)

    [Fact]
    public void AddItem_ShouldAddItemWithSnapshotedPrice()
    {
        // Arrange
        var order = OrderFaker.Generate();
        var productId = Guid.NewGuid();
        var snapshotTitle = "PS5 Digital Edition";
        var snapshotPrice = Money.Create(499.99m);
        var quantity = 2;

        // Act
        order.AddItem(productId, snapshotTitle, snapshotPrice, quantity);

        // Assert
        order.Items.ShouldHaveSingleItem();
        var item = order.Items.First();
        item.ProductId.ShouldBe(productId);
        item.AppliedTitle.ShouldBe(snapshotTitle);
        item.AppliedPrice.ShouldBe(snapshotPrice);
        item.Quantity.ShouldBe(quantity);
    }

    [Fact]
    public void AddItem_ShouldRecalculateTotal()
    {
        // Arrange
        var order = OrderFaker.Generate();
        var price = Money.Create(100m);

        // Act
        order.AddItem(Guid.NewGuid(), "Product 1", price, 2);
        order.AddItem(Guid.NewGuid(), "Product 2", price, 3);

        // Assert
        order.TotalAmount.Amount.ShouldBe(500m); // (100*2) + (100*3)
    }

    [Fact]
    public void AddItem_SameProduct_ShouldIncreaseQuantity()
    {
        // Arrange
        var order = OrderFaker.Generate();
        var productId = Guid.NewGuid();
        var price = Money.Create(100m);

        // Act
        order.AddItem(productId, "Product", price, 2);
        order.AddItem(productId, "Product", price, 3);

        // Assert
        order.Items.ShouldHaveSingleItem();
        order.Items.First().Quantity.ShouldBe(5);
    }

    [Fact]
    public void AddItem_WhenNotPending_ShouldThrowException()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.MarkAsPaid(Guid.NewGuid());

        // Act & Assert
        Should.Throw<InvalidOperationException>(() =>
                order.AddItem(Guid.NewGuid(), "New Product", Money.Create(10), 1))
            .Message.ShouldContain("non-pending");
    }

    #endregion

    #region Status Workflow Tests

    [Fact]
    public void MarkAsPaid_WhenPending_ShouldTransitionToPaid()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.ClearDomainEvents();

        // Act
        order.MarkAsPaid(Guid.NewGuid());

        // Assert
        order.Status.ShouldBe(OrderStatus.Paid);
        order.PaidAt.ShouldNotBeNull();
        order.DomainEvents.ShouldContain(e => e is OrderPaidDomainEvent);
    }

    [Fact]
    public void MarkAsPaid_WhenNotPending_ShouldThrowException()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.MarkAsPaid(Guid.NewGuid());

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => order.MarkAsPaid(Guid.NewGuid()));
    }

    [Fact]
    public void StartProcessing_WhenPaid_ShouldTransitionToProcessing()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.MarkAsPaid(Guid.NewGuid());
        order.ClearDomainEvents();

        // Act
        order.StartProcessing();

        // Assert
        order.Status.ShouldBe(OrderStatus.Processing);
        order.DomainEvents.ShouldContain(e => e is OrderProcessingStartedDomainEvent);
    }

    [Fact]
    public void StartProcessing_WhenNotPaid_ShouldThrowException()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => order.StartProcessing());
    }

    [Fact]
    public void MarkAsShipped_WhenProcessing_ShouldTransitionToShipped()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.MarkAsPaid(Guid.NewGuid());
        order.StartProcessing();
        order.ClearDomainEvents();

        // Act
        order.MarkAsShipped("TRACK-123");

        // Assert
        order.Status.ShouldBe(OrderStatus.Shipped);
        order.ShippedAt.ShouldNotBeNull();

        var shippedEvent = order.DomainEvents.OfType<OrderShippedDomainEvent>().Single();
        shippedEvent.TrackingNumber.ShouldBe("TRACK-123");
    }

    [Fact]
    public void MarkAsDelivered_WhenShipped_ShouldTransitionToDelivered()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.MarkAsPaid(Guid.NewGuid());
        order.StartProcessing();
        order.MarkAsShipped();
        order.ClearDomainEvents();

        // Act
        order.MarkAsDelivered();

        // Assert
        order.Status.ShouldBe(OrderStatus.Delivered);
        order.DeliveredAt.ShouldNotBeNull();
        order.DomainEvents.ShouldContain(e => e is OrderDeliveredDomainEvent);
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public void Cancel_WhenPending_ShouldTransitionToCancelled()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.ClearDomainEvents();
        var reason = "Customer requested cancellation";

        // Act
        order.Cancel(reason);

        // Assert
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancelledAt.ShouldNotBeNull();
        order.CancellationReason.ShouldBe(reason);
    }

    [Fact]
    public void Cancel_ShouldRaise_OrderCancelledDomainEvent_WithPreviousStatus()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.MarkAsPaid(Guid.NewGuid());
        order.ClearDomainEvents();

        // Act
        order.Cancel("Out of stock");

        // Assert
        var cancelledEvent = order.DomainEvents.OfType<OrderCancelledDomainEvent>().Single();
        cancelledEvent.PreviousStatus.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public void Cancel_WhenDelivered_ShouldThrowException()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.MarkAsPaid(Guid.NewGuid());
        order.StartProcessing();
        order.MarkAsShipped();
        order.MarkAsDelivered();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => order.Cancel("Too late"));
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ShouldThrowException()
    {
        // Arrange
        var order = OrderFaker.GenerateWithItems();
        order.Cancel("First cancellation");

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => order.Cancel("Second cancellation"));
    }

    #endregion
}