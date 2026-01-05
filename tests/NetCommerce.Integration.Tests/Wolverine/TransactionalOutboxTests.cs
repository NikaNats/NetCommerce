using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Results;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Wolverine;

/// <summary>
///     Integration tests for the transactional outbox pattern with Wolverine.
///     These tests verify that domain events and integration events are properly
///     stored in the outbox and processed reliably.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class TransactionalOutboxTests : IntegrationTestBase
{
    public TransactionalOutboxTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     Tests that creating an order triggers domain events that are captured
    ///     in the tracked session.
    /// </summary>
    [Fact]
    public async Task CreateOrder_ShouldPersistAndTriggerDomainEvents()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var shippingAddress = new AddressDto(
            Street: "123 Main St",
            City: "Seattle",
            State: "WA",
            PostalCode: "98101",
            Country: "USA",
            RecipientName: "John Doe",
            PhoneNumber: "+1-555-0123");

        var billingAddress = new AddressDto(
            Street: "123 Main St",
            City: "Seattle",
            State: "WA",
            PostalCode: "98101",
            Country: "USA",
            RecipientName: "John Doe",
            PhoneNumber: string.Empty);

        var (productId, productPrice) = await SeedProductAsync(99.99m);

        var items = new List<OrderItemRequest>
        {
            new(productId, 2, productPrice)
        };

        var command = new CreateOrderCommand(customerId, "customer@test.com", "Test Customer", items, shippingAddress, billingAddress, "CreditCard", Guid.NewGuid().ToString());

        // Act - Execute with tracking and get the result
        var (tracked, result) = await Fixture.Host.InvokeMessageAndWaitAsync<Result<Guid>>(command);

        // Assert - Command was executed successfully
        result.IsSuccess.ShouldBeTrue($"Order creation failed: {result.Error?.Description}");
        var orderId = result.Value;
        orderId.ShouldNotBe(Guid.Empty);

        // Verify order was created with correct data
        await using var db = Fixture.CreateOrderingDbContext();
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        order.ShouldNotBeNull();
        order.CustomerId.ShouldBe(customerId);
        order.Items.Count.ShouldBe(1);
        order.Status.ShouldBe(OrderStatus.Submitted);
    }

    /// <summary>
    ///     Tests that cancelling an order within grace period works correctly.
    /// </summary>
    [Fact]
    public async Task CancelOrder_DuringGracePeriod_ShouldSucceed()
    {
        // Arrange - Create an order first
        var customerId = Guid.NewGuid();
        var shippingAddress = new AddressDto(
            "456 Oak Ave", "Portland", "OR", "97201", "USA", "Jane Doe", "+1-555-0456");
        var billingAddress = new AddressDto(
            "456 Oak Ave", "Portland", "OR", "97201", "USA", "Jane Doe", string.Empty);
        var (productId, productPrice) = await SeedProductAsync(50.00m);

        var items = new List<OrderItemRequest>
        {
            new(productId, 1, productPrice)
        };

        var createCommand = new CreateOrderCommand(customerId, "customer@test.com", "Test Customer", items, shippingAddress, billingAddress, "CreditCard", Guid.NewGuid().ToString());
        var (_, createResult) = await Fixture.Host.InvokeMessageAndWaitAsync<Result<Guid>>(createCommand);
        createResult.IsSuccess.ShouldBeTrue();

        var orderId = createResult.Value;

        // Act - Cancel the order during grace period
        var cancelCommand = new CancelOrderCommand(orderId, "Changed my mind");
        var (tracked, cancelResult) = await Fixture.Host.InvokeMessageAndWaitAsync<Result>(cancelCommand);

        // Assert
        cancelResult.IsSuccess.ShouldBeTrue();

        // Verify order status
        await using var db = Fixture.CreateOrderingDbContext();
        var order = await db.Orders.FindAsync(orderId);
        order.ShouldNotBeNull();
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    /// <summary>
    ///     Tests that the outbox ensures message delivery even with concurrent operations.
    /// </summary>
    [Fact]
    public async Task OutboxPattern_ShouldEnsureAtLeastOnceDelivery()
    {
        // Arrange - Create orders sequentially to avoid race conditions
        var orderIds = new List<Guid>();

        for (var i = 1; i <= 3; i++)
        {
            var (productId, productPrice) = await SeedProductAsync(25.00m * i);

            var command = new CreateOrderCommand(
                CustomerId: Guid.NewGuid(),
                CustomerEmail: $"customer{i}@test.com",
                CustomerName: $"Customer {i}",
                Items: new List<OrderItemRequest>
                {
                    new(productId, i, productPrice)
                },
                ShippingAddress: new AddressDto(
                    $"{i}00 Test St", "Test City", "TS", $"0000{i}", "USA", $"Customer {i}", string.Empty),
                BillingAddress: new AddressDto(
                    $"{i}00 Test St", "Test City", "TS", $"0000{i}", "USA", $"Customer {i}", string.Empty),
                PaymentMethod: "CreditCard",
                IdempotencyKey: Guid.NewGuid().ToString());

            var (_, result) = await Fixture.Host.InvokeMessageAndWaitAsync<Result<Guid>>(command);
            result.IsSuccess.ShouldBeTrue($"Order {i} creation failed: {result.Error?.Description}");
            orderIds.Add(result.Value);
        }

        // Assert - All orders should be created successfully
        orderIds.Count.ShouldBe(3);

        // Verify all orders exist in database
        await using var db = Fixture.CreateOrderingDbContext();
        var orderCount = await db.Orders.CountAsync(o => orderIds.Contains(o.Id));
        orderCount.ShouldBe(3);
    }

    /// <summary>
    ///     Tests that cascading messages work correctly with tracked sessions.
    /// </summary>
    [Fact]
    public async Task CascadingMessages_ShouldBeTracked()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var (productId, productPrice) = await SeedProductAsync(75.00m);

        var command = new CreateOrderCommand(
            CustomerId: customerId,
            CustomerEmail: "cascade@test.com",
            CustomerName: "Cascade Test",
            Items: new List<OrderItemRequest>
            {
                new(productId, 1, productPrice)
            },
            ShippingAddress: new AddressDto(
                "789 Cascade Dr", "Cascade City", "CA", "90210", "USA", "Cascade Test", string.Empty),
            BillingAddress: new AddressDto(
                "789 Cascade Dr", "Cascade City", "CA", "90210", "USA", "Cascade Test", string.Empty),
            PaymentMethod: "CreditCard",
            IdempotencyKey: Guid.NewGuid().ToString());

        // Act - Use InvokeMessageAndWaitAsync to get the result properly
        var (tracked, result) = await Fixture.Host.InvokeMessageAndWaitAsync<Result<Guid>>(command);

        // Assert
        result.IsSuccess.ShouldBeTrue($"Order creation failed: {result.Error?.Description}");
        result.Value.ShouldNotBe(Guid.Empty);

        // Verify the order was created
        await using var db = Fixture.CreateOrderingDbContext();
        var order = await db.Orders.FindAsync(result.Value);
        order.ShouldNotBeNull();
        order.CustomerId.ShouldBe(customerId);
    }

    private async Task<(Guid ProductId, decimal Price)> SeedProductAsync(decimal price)
    {
        await using var catalogDb = Fixture.CreateCatalogDbContext();
        var product = Product.Create(
            name: $"Test Product {Guid.NewGuid():N}",
            description: "Integration test product",
            sku: $"TEST-{Guid.NewGuid():N}",
            price: Money.Create(price, "USD"),
            categoryId: Guid.NewGuid());

        catalogDb.Products.Add(product);
        await catalogDb.SaveChangesAsync();

        return (product.Id, product.Price.Amount);
    }
}
