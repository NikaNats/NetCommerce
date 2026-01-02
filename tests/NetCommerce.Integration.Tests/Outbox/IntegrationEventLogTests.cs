using System.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Integration.Tests.Outbox;

/// <summary>
///     Integration tests for the Integration Event Log (Auditability).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class IntegrationEventLogTests : IntegrationTestBase
{
    public IntegrationEventLogTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

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

    #region Basic Event Log Creation Tests

    [Fact]
    public async Task SaveChangesAsync_WithDomainEvents_ShouldCreateIntegrationEventLogEntries()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);

        // Act
        await context.SaveChangesAsync();

        // Assert - Integration event log should be created
        var eventLogs = await context.IntegrationEventLogs.ToListAsync();
        eventLogs.ShouldNotBeEmpty();
        eventLogs.ShouldAllBe(e => e.Status == IntegrationEventLogStatus.Pending);
        eventLogs.ShouldAllBe(e => e.Direction == IntegrationEventLogDirection.Published);

        // Verify the event type is recorded correctly
        var log = eventLogs.First();
        log.EventType.ShouldContain("OrderSubmittedDomainEvent");
        log.Content.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_WithActiveActivity_ShouldCaptureTraceIdAndSpanId()
    {
        // Arrange
        using var activity = new Activity("TestOperation");
        activity.Start();

        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);

        // Act
        await context.SaveChangesAsync();

        // Assert - TraceId and SpanId should be captured
        var eventLog = await context.IntegrationEventLogs.FirstAsync();

        eventLog.TraceId.ShouldNotBeNullOrEmpty();
        eventLog.SpanId.ShouldNotBeNullOrEmpty();
        eventLog.TraceId.ShouldBe(activity.TraceId.ToString());
        eventLog.SpanId.ShouldBe(activity.SpanId.ToString());
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutActiveActivity_ShouldHaveNullTraceContext()
    {
        // Arrange - Ensure no active Activity
        Activity.Current = null;

        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);

        // Act
        await context.SaveChangesAsync();

        // Assert - TraceId should be null when no activity
        var eventLog = await context.IntegrationEventLogs.FirstAsync();

        eventLog.TraceId.ShouldBeNull();
        eventLog.SpanId.ShouldBeNull();
    }

    [Fact]
    public async Task IntegrationEventLog_ShouldCorrelateWithOutboxMessage()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);

        // Act
        await context.SaveChangesAsync();

        // Assert - Both outbox message and event log should exist with same EventId
        var outboxMessage = await context.OutboxMessages.FirstAsync();
        var eventLog = await context.IntegrationEventLogs.FirstAsync();

        outboxMessage.EventId.ShouldBe(eventLog.EventId);
    }

    [Fact]
    public async Task IntegrationEventLog_ShouldRecordOccurredOnTimestamp()
    {
        // Arrange
        var beforeTest = DateTime.UtcNow;
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var eventLog = await context.IntegrationEventLogs.FirstAsync();

        eventLog.OccurredOn.ShouldBeGreaterThanOrEqualTo(beforeTest);
        eventLog.LoggedAt.ShouldBeGreaterThanOrEqualTo(eventLog.OccurredOn);
    }

    [Fact]
    public async Task MultipleDomainEvents_ShouldCreateMultipleIntegrationEventLogEntries()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order1 = CreateTestOrder("1");
        var order2 = CreateTestOrder("2");

        context.Orders.Add(order1);
        context.Orders.Add(order2);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var eventLogs = await context.IntegrationEventLogs.ToListAsync();
        eventLogs.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region IntegrationEventLogService Tests

    [Fact]
    public async Task IntegrationEventLogService_MarkAsPublished_ShouldUpdateStatus()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var eventLog = await context.IntegrationEventLogs.FirstAsync();
        var service = new IntegrationEventLogService<OrderingDbContext>(context);

        // Act
        await service.MarkEventAsPublishedAsync(eventLog.EventId);

        // Assert
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var updatedLog = await verifyContext.IntegrationEventLogs.FirstAsync(e => e.EventId == eventLog.EventId);
        updatedLog.Status.ShouldBe(IntegrationEventLogStatus.Published);
        updatedLog.TimesSent.ShouldBe(1);
        updatedLog.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task IntegrationEventLogService_MarkAsFailed_ShouldUpdateStatusAndError()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var eventLog = await context.IntegrationEventLogs.FirstAsync();
        var service = new IntegrationEventLogService<OrderingDbContext>(context);
        var errorMessage = "Connection timeout";

        // Act
        await service.MarkEventAsFailedAsync(eventLog.EventId, errorMessage);

        // Assert
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var updatedLog = await verifyContext.IntegrationEventLogs.FirstAsync(e => e.EventId == eventLog.EventId);
        updatedLog.Status.ShouldBe(IntegrationEventLogStatus.Failed);
        updatedLog.Error.ShouldBe(errorMessage);
        updatedLog.TimesSent.ShouldBe(1);
    }

    [Fact]
    public async Task IntegrationEventLogService_MarkAsPublished_OnlyUpdatesIfPending()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var eventLog = await context.IntegrationEventLogs.FirstAsync();
        var service = new IntegrationEventLogService<OrderingDbContext>(context);

        // First mark as published
        await service.MarkEventAsPublishedAsync(eventLog.EventId);

        // Act - Try to mark as published again (should not update TimesSent)
        await service.MarkEventAsPublishedAsync(eventLog.EventId);

        // Assert
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var updatedLog = await verifyContext.IntegrationEventLogs.FirstAsync(e => e.EventId == eventLog.EventId);
        updatedLog.TimesSent.ShouldBe(1); // Should still be 1, not incremented
    }

    [Fact]
    public async Task IntegrationEventLogService_MarkAsInProgress_ShouldKeepPendingStatus()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var eventLog = await context.IntegrationEventLogs.FirstAsync();
        var service = new IntegrationEventLogService<OrderingDbContext>(context);

        // Act
        await service.MarkEventAsInProgressAsync(eventLog.EventId);

        // Assert
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var updatedLog = await verifyContext.IntegrationEventLogs.FirstAsync(e => e.EventId == eventLog.EventId);
        updatedLog.Status.ShouldBe(IntegrationEventLogStatus.Pending);
    }

    #endregion

    #region OutboxProcessor Integration with Event Log Tests

    [Fact]
    public async Task OutboxProcessor_WhenProcessingSucceeds_ShouldMarkEventLogAsPublished()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Verify initial state
        var initialEventLog = await context.IntegrationEventLogs.FirstAsync();
        initialEventLog.Status.ShouldBe(IntegrationEventLogStatus.Pending);

        // Setup processor with working mediator
        var mediator = Substitute.For<IMediator>();
        var services = new ServiceCollection();
        services.AddDbContext<OrderingDbContext>(options =>
            options.UseNpgsql(Fixture.PostgresConnectionString));
        services.AddSingleton(mediator);
        services.AddScoped<IIntegrationEventLogService, IntegrationEventLogService<OrderingDbContext>>();

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 3,
            Enabled = true
        });

        var processor = new TestableOutboxProcessorWithEventLog(scopeFactory, logger, options);

        // Act
        await processor.ProcessMessagesOnceAsync(CancellationToken.None);

        // Assert - Event log should be marked as Published
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var updatedEventLog = await verifyContext.IntegrationEventLogs.FirstAsync(e => e.EventId == initialEventLog.EventId);
        updatedEventLog.Status.ShouldBe(IntegrationEventLogStatus.Published);
        updatedEventLog.TimesSent.ShouldBe(1);
    }

    [Fact]
    public async Task OutboxProcessor_WhenProcessingFailsWithMaxRetries_ShouldMarkEventLogAsFailed()
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

        var services = new ServiceCollection();
        services.AddDbContext<OrderingDbContext>(options =>
            options.UseNpgsql(Fixture.PostgresConnectionString));
        services.AddSingleton(mediator);
        services.AddScoped<IIntegrationEventLogService, IntegrationEventLogService<OrderingDbContext>>();

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var logger = Substitute.For<ILogger<OutboxProcessor<OrderingDbContext>>>();
        var options = Options.Create(new OutboxProcessorOptions
        {
            PollingIntervalMs = 100,
            BatchSize = 10,
            MaxRetries = 2, // Low max retries for faster test
            Enabled = true
        });

        var processor = new TestableOutboxProcessorWithEventLog(scopeFactory, logger, options);

        // Act - Process multiple times to exceed max retries
        for (var i = 0; i < 3; i++)
        {
            await processor.ProcessMessagesOnceAsync(CancellationToken.None);
        }

        // Assert - Event log should be marked as Failed
        await using var verifyContext = Fixture.CreateOrderingDbContext();
        var eventLog = await verifyContext.IntegrationEventLogs.FirstAsync();
        eventLog.Status.ShouldBe(IntegrationEventLogStatus.Failed);
        eventLog.Error.ShouldNotBeNullOrEmpty();
    }

    #endregion

    #region Atomicity Tests

    [Fact]
    public async Task SaveChangesAsync_ShouldSaveOutboxAndEventLogAtomically()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);

        // Act
        await context.SaveChangesAsync();

        // Assert - Both should exist
        var outboxCount = await context.OutboxMessages.CountAsync();
        var eventLogCount = await context.IntegrationEventLogs.CountAsync();

        outboxCount.ShouldBe(eventLogCount);
        outboxCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SaveChangesAsync_AllEventLogsShouldHaveMatchingOutboxMessages()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        // Create multiple orders
        for (var i = 0; i < 5; i++)
        {
            context.Orders.Add(CreateTestOrder(i.ToString()));
        }

        // Act
        await context.SaveChangesAsync();

        // Assert - Every event log should have a matching outbox message
        var eventLogs = await context.IntegrationEventLogs.ToListAsync();
        var outboxMessages = await context.OutboxMessages.ToListAsync();

        foreach (var eventLog in eventLogs)
        {
            outboxMessages.ShouldContain(o => o.EventId == eventLog.EventId);
        }
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task IntegrationEventLogRepository_GetByEventId_ShouldReturnCorrectLog()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var eventLog = await context.IntegrationEventLogs.FirstAsync();
        var repository = new IntegrationEventLogRepository<OrderingDbContext>(context);

        // Act
        var result = await repository.GetByEventIdAsync(eventLog.EventId);

        // Assert
        result.ShouldNotBeEmpty();
        result.First().EventId.ShouldBe(eventLog.EventId);
    }

    [Fact]
    public async Task IntegrationEventLogRepository_GetByTraceId_ShouldReturnCorrectLogs()
    {
        // Arrange
        using var activity = new Activity("TestOperation");
        activity.Start();

        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var repository = new IntegrationEventLogRepository<OrderingDbContext>(context);

        // Act
        var result = await repository.GetByTraceIdAsync(activity.TraceId.ToString());

        // Assert
        result.ShouldNotBeEmpty();
        result.ShouldAllBe(e => e.TraceId == activity.TraceId.ToString());
    }

    [Fact]
    public async Task IntegrationEventLogRepository_GetByDirectionAndStatus_ShouldFilterCorrectly()
    {
        // Arrange
        await using var context = Fixture.CreateOrderingDbContext();

        var order = CreateTestOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var repository = new IntegrationEventLogRepository<OrderingDbContext>(context);

        // Act
        var result = await repository.GetByDirectionAndStatusAsync(
            IntegrationEventLogDirection.Published,
            IntegrationEventLogStatus.Pending);

        // Assert
        result.ShouldNotBeEmpty();
        result.ShouldAllBe(e => e.Direction == IntegrationEventLogDirection.Published);
        result.ShouldAllBe(e => e.Status == IntegrationEventLogStatus.Pending);
    }

    #endregion
}

/// <summary>
///     Testable version of OutboxProcessor that includes IntegrationEventLogService.
/// </summary>
internal class TestableOutboxProcessorWithEventLog : OutboxProcessor<OrderingDbContext>
{
    public TestableOutboxProcessorWithEventLog(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor<OrderingDbContext>> logger,
        IOptions<OutboxProcessorOptions> options)
        : base(scopeFactory, logger, options)
    {
    }

    public async Task ProcessMessagesOnceAsync(CancellationToken cancellationToken)
    {
        var method = typeof(OutboxProcessor<OrderingDbContext>)
            .GetMethod("ProcessOutboxMessagesAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method != null)
        {
            var task = (Task?)method.Invoke(this, [cancellationToken]);
            if (task != null) await task;
        }
    }
}
