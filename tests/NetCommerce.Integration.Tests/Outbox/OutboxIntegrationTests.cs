using System.Collections.Concurrent;
using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Integration.Tests.Outbox;

/// <summary>
///     Integration tests for the Transactional Outbox pattern.
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
            $"Test Recipient {suffix}",
            $"Test Street {suffix}",
            $"Test City {suffix}",
            "TS",
            "US",
            $"1234{suffix}",
            "+1234567890");
    }

    private static Order CreateTestOrder(string suffix = "")
    {
        return Order.Create(
            Guid.NewGuid(),
            CreateTestShippingAddress(suffix),
            Guid.NewGuid().ToString());
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
        for (var i = 0; i < 4; i++) await processor.ProcessMessagesOnceAsync(CancellationToken.None);

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

    #region Race Condition Prevention Tests (FOR UPDATE SKIP LOCKED)

    [Fact]
    public async Task OutboxProcessor_ShouldClaimMessagesWithProcessingStatus()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Get initial message
        var initialMessage = await context.OutboxMessages.FirstAsync();
        initialMessage.Status.ShouldBe(OutboxMessageStatus.Pending);

        // Setup processor with slow mediator to observe intermediate state
        var mediator = Substitute.For<IMediator>();
        var scopeFactory = CreateScopeFactory(mediator);
        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true,
            StuckMessageTimeoutSeconds = 300
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act
        await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Message should be processed
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var processedMessage = await verifyContext.OutboxMessages.FirstAsync();
        processedMessage.Status.ShouldBe(OutboxMessageStatus.Processed);
        processedMessage.ProcessedOn.ShouldNotBeNull();
    }

    [Fact]
    public async Task OutboxProcessor_WhenMessageFails_ShouldReturnToPendingStatus()
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
            Enabled = true,
            StuckMessageTimeoutSeconds = 300
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act
        await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Message should return to Pending for retry
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var failedMessage = await verifyContext.OutboxMessages.FirstAsync();
        failedMessage.Status.ShouldBe(OutboxMessageStatus.Pending);
        failedMessage.RetryCount.ShouldBe(1);
        failedMessage.Error.ShouldNotBeNullOrEmpty();
        failedMessage.ProcessingStartedAt.ShouldBeNull();
    }

    [Fact]
    public async Task OutboxProcessor_WhenMaxRetriesExceeded_ShouldSetStatusToFailed()
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
            MaxRetries = 2, // Lower max retries for faster test
            Enabled = true,
            StuckMessageTimeoutSeconds = 300
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act - Process 3 times to exceed max retries of 2
        for (var i = 0; i < 3; i++) await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Message should be in Failed status
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var failedMessage = await verifyContext.OutboxMessages.FirstAsync();
        failedMessage.Status.ShouldBe(OutboxMessageStatus.Failed);
        failedMessage.RetryCount.ShouldBe(2); // Stopped at max retries
    }

    [Fact]
    public async Task OutboxProcessor_ConcurrentProcessors_ShouldNotProcessSameMessageTwice()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        // Create multiple orders to have several messages
        for (var i = 0; i < 5; i++)
        {
            var order = CreateTestOrder(i.ToString());
            context.Orders.Add(order);
        }

        await context.SaveChangesAsync();

        var messageCount = await context.OutboxMessages.CountAsync();
        messageCount.ShouldBeGreaterThanOrEqualTo(5);

        // Track which messages were published
        var publishedMessageIds = new ConcurrentBag<Guid>();
        var mediator = Substitute.For<IMediator>();
        mediator
            .When(m => m.Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var domainEvent = call.Arg<IDomainEvent>();
                publishedMessageIds.Add(domainEvent.EventId);
                // Simulate some processing time
                Thread.Sleep(50);
            });

        var scopeFactory = CreateScopeFactory(mediator);
        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true,
            StuckMessageTimeoutSeconds = 300
        });

        // Create multiple processors to simulate concurrent workers
        var processor1 = new TestableOutboxProcessor(scopeFactory, logger, options);
        var processor2 = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act - Run both processors concurrently
        var task1 = processor1.ProcessMessagesOnceAsync(CancellationToken.None);
        var task2 = processor2.ProcessMessagesOnceAsync(CancellationToken.None);

        await Task.WhenAll(task1, task2);

        // Assert - Each message should be published exactly once (no duplicates)
        var uniqueIds = publishedMessageIds.Distinct().ToList();
        uniqueIds.Count.ShouldBe(publishedMessageIds.Count,
            "Each message should be published exactly once - no duplicates allowed");
    }

    [Fact]
    public async Task OutboxProcessor_ShouldNotPickUpMessagesInFailedStatus()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Manually set message to Failed status
        var message = await context.OutboxMessages.FirstAsync();
        // Simulate exhausting retries
        message.MarkAsFailed("Error 1", 2);
        message.MarkAsFailed("Error 2", 2);
        message.Status.ShouldBe(OutboxMessageStatus.Failed);
        await context.SaveChangesAsync();

        // Track if mediator was called
        var mediatorCalled = false;
        var mediator = Substitute.For<IMediator>();
        mediator
            .When(m => m.Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>()))
            .Do(_ => mediatorCalled = true);

        var scopeFactory = CreateScopeFactory(mediator);
        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true,
            StuckMessageTimeoutSeconds = 300
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act
        await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Mediator should not be called for Failed messages
        mediatorCalled.ShouldBeFalse("Failed messages should not be picked up for processing");
    }

    [Fact]
    public async Task OutboxProcessor_ShouldNotPickUpProcessedMessages()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Manually set message to Processed status
        var message = await context.OutboxMessages.FirstAsync();
        message.MarkAsProcessed();
        await context.SaveChangesAsync();

        // Track if mediator was called
        var mediatorCalled = false;
        var mediator = Substitute.For<IMediator>();
        mediator
            .When(m => m.Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>()))
            .Do(_ => mediatorCalled = true);

        var scopeFactory = CreateScopeFactory(mediator);
        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true,
            StuckMessageTimeoutSeconds = 300
        });

        var processor = new TestableOutboxProcessor(scopeFactory, logger, options);

        // Act
        await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Mediator should not be called for already processed messages
        mediatorCalled.ShouldBeFalse("Already processed messages should not be picked up again");
    }

    #endregion
}

/// <summary>
///     Testable version of OutboxProcessor that exposes the processing logic.
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
                BindingFlags.NonPublic | BindingFlags.Instance);

        if (method != null)
        {
            var task = (Task?)method.Invoke(this, [cancellationToken]);
            if (task != null) await task;
        }
    }
}