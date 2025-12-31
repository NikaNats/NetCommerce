using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

namespace NetCommerce.Integration.Tests.Outbox;

/// <summary>
/// Integration tests for the Transactional Outbox pattern.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class OutboxIntegrationTests : IntegrationTestBase
{
    public OutboxIntegrationTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Helper Methods

    private static ShippingAddress CreateTestShippingAddress(string suffix = "")
    {
        return ShippingAddress.Create(
            recipientName: $"Test Recipient {suffix}",
            street: $"Test Street {suffix}",
            city: $"Test City {suffix}",
            state: "TS",
            country: "US",
            postalCode: $"1234{suffix}",
            phone: "+1234567890");
    }

    private static Order CreateTestOrder(string suffix = "")
    {
        return Order.Create(
            customerId: Guid.NewGuid(),
            shippingAddress: CreateTestShippingAddress(suffix),
            idempotencyKey: Guid.NewGuid().ToString());
    }

    private IServiceScopeFactory CreateScopeFactory(IMediator mediator)
    {
        var services = new ServiceCollection();
        
        services.AddDbContext<OrderingDbContext>(options =>
            options.UseNpgsql(Fixture.PostgresConnectionString));
        
        services.AddSingleton(mediator);

        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IServiceScopeFactory>();
    }

    #endregion

    #region BaseDbContext Outbox Tests

    [Fact]
    public async Task SaveChangesAsync_WithDomainEvents_ShouldCreateOutboxMessages()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        
        // Create an order which raises domain events
        var order = CreateTestOrder();
        context.Orders.Add(order);

        // Act
        await context.SaveChangesAsync();

        // Assert - Outbox message should be created
        var outboxMessages = await context.OutboxMessages.ToListAsync();
        outboxMessages.ShouldNotBeEmpty();
        outboxMessages.ShouldAllBe(m => m.ProcessedOn == null);
        
        // Verify the event type is serialized correctly
        var message = outboxMessages.First();
        message.Type.ShouldContain("OrderCreatedDomainEvent");
        message.Content.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_WithMultipleDomainEvents_ShouldCreateMultipleOutboxMessages()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        
        // Create multiple orders
        var order1 = CreateTestOrder("1");
        var order2 = CreateTestOrder("2");
        
        context.Orders.Add(order1);
        context.Orders.Add(order2);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var outboxMessages = await context.OutboxMessages.ToListAsync();
        outboxMessages.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SaveChangesAsync_DomainEventsAndEntityChanges_ShouldBeSavedAtomically()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        
        var order = CreateTestOrder();
        context.Orders.Add(order);

        // Act
        await context.SaveChangesAsync();

        // Assert - Both order and outbox message exist
        var savedOrder = await context.Orders.FirstOrDefaultAsync(o => o.Id == order.Id);
        var outboxMessage = await context.OutboxMessages.FirstOrDefaultAsync();

        savedOrder.ShouldNotBeNull();
        outboxMessage.ShouldNotBeNull();
    }

    #endregion

    #region OutboxProcessor Tests

    [Fact]
    public async Task OutboxProcessor_ShouldProcessUnprocessedMessages()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        
        // Create an order to generate outbox messages
        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Verify outbox message exists
        var unprocessedMessages = await context.OutboxMessages
            .Where(m => m.ProcessedOn == null)
            .ToListAsync();
        unprocessedMessages.ShouldNotBeEmpty();

        // Setup processor
        var mediator = Substitute.For<IMediator>();
        var scopeFactory = CreateScopeFactory(mediator);
        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act
        await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Messages should be processed
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var processedMessages = await verifyContext.OutboxMessages
            .Where(m => m.ProcessedOn != null)
            .ToListAsync();
        
        processedMessages.ShouldNotBeEmpty();
        
        // Verify MediatR was called
        await mediator.Received().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboxProcessor_WhenMediatorFails_ShouldMarkMessageAsFailed()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        
        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Setup processor with failing mediator
        var mediator = Substitute.For<IMediator>();
        mediator
            .When(m => m.Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Handler failed"));

        var scopeFactory = CreateScopeFactory(mediator);
        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act
        await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Message should be marked as failed with error
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var failedMessage = await verifyContext.OutboxMessages.FirstOrDefaultAsync();
        
        failedMessage.ShouldNotBeNull();
        failedMessage.ProcessedOn.ShouldBeNull();
        failedMessage.Error.ShouldNotBeNullOrEmpty();
        failedMessage.RetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task OutboxProcessor_ShouldRespectMaxRetries()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        
        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Setup processor with failing mediator
        var mediator = Substitute.For<IMediator>();
        mediator
            .When(m => m.Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Handler failed"));

        var scopeFactory = CreateScopeFactory(mediator);
        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act - Process 4 times (exceeds max retries of 3)
        for (var i = 0; i < 4; i++)
        {
            await processor.ProcessMessagesOnceAsync(CancellationToken.None);
        }

        // Assert - Message should not be picked up after max retries
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var message = await verifyContext.OutboxMessages.FirstOrDefaultAsync();
        
        message.ShouldNotBeNull();
        message.RetryCount.ShouldBe(3); // Should stop at max retries
        message.ProcessedOn.ShouldBeNull();
    }

    [Fact]
    public async Task OutboxProcessor_ShouldProcessMessagesInOrder()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();
        
        // Create multiple orders with slight delays to ensure different OccurredOn times
        for (var i = 0; i < 3; i++)
        {
            var order = CreateTestOrder(i.ToString());
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            await Task.Delay(10); // Small delay to ensure ordering
        }

        // Track order of published events
        var publishedOrder = new List<DateTime>();
        var mediator = Substitute.For<IMediator>();
        mediator
            .When(m => m.Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var domainEvent = call.Arg<IDomainEvent>();
                publishedOrder.Add(domainEvent.OccurredOn);
            });

        var scopeFactory = CreateScopeFactory(mediator);
        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act
        await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Events should be published in order
        publishedOrder.Count.ShouldBeGreaterThanOrEqualTo(3);
        publishedOrder.ShouldBe(publishedOrder.OrderBy(d => d).ToList());
    }

    #endregion
}

/// <summary>
/// Testable version of OutboxProcessor that exposes the processing logic.
/// </summary>
internal class TestableOutboxProcessor : OutboxProcessor<OrderingDbContext>
{
    public TestableOutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor<OrderingDbContext>> logger,
        IOptions<OutboxProcessorOptions> options)
        : base(scopeFactory, logger, options)
    {
    }

    public async Task ProcessMessagesOnceAsync(CancellationToken cancellationToken)
    {
        // Use reflection to call the private method
        var method = typeof(OutboxProcessor<OrderingDbContext>)
            .GetMethod("ProcessOutboxMessagesAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (method != null)
        {
            var task = (Task?)method.Invoke(this, [cancellationToken]);
            if (task != null)
            {
                await task;
            }
        }
    }
}
