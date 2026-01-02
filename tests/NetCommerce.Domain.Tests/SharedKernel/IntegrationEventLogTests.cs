using System.Diagnostics;
using NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;
using Shouldly;

namespace NetCommerce.Domain.Tests.SharedKernel;

/// <summary>
///     Unit tests for IntegrationEventLog entity.
/// </summary>
public class IntegrationEventLogTests
{
    #region CreatePending Tests

    [Fact]
    public void CreatePending_ShouldSetAllRequiredProperties()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var eventType = "OrderSubmittedDomainEvent";
        var content = """{"orderId":"123"}""";
        var occurredOn = DateTime.UtcNow.AddMinutes(-5);

        // Act
        var log = IntegrationEventLog.CreatePending(eventId, eventType, content, occurredOn);

        // Assert
        log.Id.ShouldNotBe(Guid.Empty);
        log.EventId.ShouldBe(eventId);
        log.EventType.ShouldBe(eventType);
        log.Content.ShouldBe(content);
        log.OccurredOn.ShouldBe(occurredOn);
        log.LoggedAt.ShouldBeGreaterThanOrEqualTo(occurredOn);
        log.Direction.ShouldBe(IntegrationEventLogDirection.Published);
        log.Status.ShouldBe(IntegrationEventLogStatus.Pending);
        log.TimesSent.ShouldBe(0);
        log.ProcessedAt.ShouldBeNull();
        log.Error.ShouldBeNull();
        log.HandlerName.ShouldBeNull();
    }

    [Fact]
    public void CreatePending_WithCorrelationId_ShouldUseProvidedCorrelationId()
    {
        // Arrange
        var correlationId = "my-correlation-id";

        // Act
        var log = IntegrationEventLog.CreatePending(
            Guid.NewGuid(),
            "TestEvent",
            "{}",
            DateTime.UtcNow,
            correlationId);

        // Assert
        log.CorrelationId.ShouldBe(correlationId);
    }

    [Fact]
    public void CreatePending_WithActiveActivity_ShouldCaptureTraceIdAndSpanId()
    {
        // Arrange
        using var activity = new Activity("TestOperation");
        activity.Start();

        // Act
        var log = IntegrationEventLog.CreatePending(
            Guid.NewGuid(),
            "TestEvent",
            "{}",
            DateTime.UtcNow);

        // Assert
        log.TraceId.ShouldBe(activity.TraceId.ToString());
        log.SpanId.ShouldBe(activity.SpanId.ToString());
    }

    [Fact]
    public void CreatePending_WithoutActiveActivity_ShouldHaveNullTraceContext()
    {
        // Arrange - Ensure no active Activity
        Activity.Current = null;

        // Act
        var log = IntegrationEventLog.CreatePending(
            Guid.NewGuid(),
            "TestEvent",
            "{}",
            DateTime.UtcNow);

        // Assert
        log.TraceId.ShouldBeNull();
        log.SpanId.ShouldBeNull();
    }

    [Fact]
    public void CreatePending_WithMetadata_ShouldStoreMetadata()
    {
        // Arrange
        var metadata = """{"source":"test"}""";

        // Act
        var log = IntegrationEventLog.CreatePending(
            Guid.NewGuid(),
            "TestEvent",
            "{}",
            DateTime.UtcNow,
            metadata: metadata);

        // Assert
        log.Metadata.ShouldBe(metadata);
    }

    #endregion

    #region CreateReceived Tests

    [Fact]
    public void CreateReceived_ShouldSetAllRequiredProperties()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var eventType = "OrderSubmittedIntegrationEvent";
        var content = """{"orderId":"123"}""";
        var occurredOn = DateTime.UtcNow.AddMinutes(-5);
        var handlerName = "OrderSubmittedHandler";

        // Act
        var log = IntegrationEventLog.CreateReceived(eventId, eventType, content, occurredOn, handlerName);

        // Assert
        log.Id.ShouldNotBe(Guid.Empty);
        log.EventId.ShouldBe(eventId);
        log.EventType.ShouldBe(eventType);
        log.Content.ShouldBe(content);
        log.OccurredOn.ShouldBe(occurredOn);
        log.Direction.ShouldBe(IntegrationEventLogDirection.Received);
        log.HandlerName.ShouldBe(handlerName);
        log.Status.ShouldBe(IntegrationEventLogStatus.Pending);
        log.TimesSent.ShouldBe(0);
    }

    [Fact]
    public void CreateReceived_WithActiveActivity_ShouldCaptureTraceIdAndSpanId()
    {
        // Arrange
        using var activity = new Activity("TestHandler");
        activity.Start();

        // Act
        var log = IntegrationEventLog.CreateReceived(
            Guid.NewGuid(),
            "TestEvent",
            "{}",
            DateTime.UtcNow,
            "TestHandler");

        // Assert
        log.TraceId.ShouldBe(activity.TraceId.ToString());
        log.SpanId.ShouldBe(activity.SpanId.ToString());
    }

    #endregion

    #region Status Transition Tests

    [Fact]
    public void MarkAsPublished_ShouldUpdateStatusAndTimesSent()
    {
        // Arrange
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow);

        // Act
        log.MarkAsPublished();

        // Assert
        log.Status.ShouldBe(IntegrationEventLogStatus.Published);
        log.TimesSent.ShouldBe(1);
        log.ProcessedAt.ShouldNotBeNull();
        log.Error.ShouldBeNull();
    }

    [Fact]
    public void MarkAsPublished_CalledMultipleTimes_ShouldIncrementTimesSent()
    {
        // Arrange
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow);

        // Act
        log.MarkAsPublished();
        log.MarkAsPublished();
        log.MarkAsPublished();

        // Assert
        log.TimesSent.ShouldBe(3);
    }

    [Fact]
    public void MarkAsFailed_ShouldUpdateStatusErrorAndTimesSent()
    {
        // Arrange
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow);
        var error = "Connection timeout";

        // Act
        log.MarkAsFailed(error);

        // Assert
        log.Status.ShouldBe(IntegrationEventLogStatus.Failed);
        log.Error.ShouldBe(error);
        log.TimesSent.ShouldBe(1);
        log.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public void MarkAsFailed_CalledMultipleTimes_ShouldIncrementTimesSent()
    {
        // Arrange
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow);

        // Act
        log.MarkAsFailed("Error 1");
        log.MarkAsFailed("Error 2");

        // Assert
        log.TimesSent.ShouldBe(2);
        log.Error.ShouldBe("Error 2"); // Should have latest error
    }

    [Fact]
    public void MarkAsProcessed_ShouldUpdateStatusAndClearError()
    {
        // Arrange
        var log = IntegrationEventLog.CreateReceived(
            Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow, "TestHandler");

        // Act
        log.MarkAsProcessed();

        // Assert
        log.Status.ShouldBe(IntegrationEventLogStatus.Processed);
        log.ProcessedAt.ShouldNotBeNull();
        log.Error.ShouldBeNull();
    }

    [Fact]
    public void MarkAsInProgress_ShouldKeepPendingStatus()
    {
        // Arrange
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow);

        // Act
        log.MarkAsInProgress();

        // Assert
        log.Status.ShouldBe(IntegrationEventLogStatus.Pending);
    }

    #endregion

    #region Timestamp Tests

    [Fact]
    public void CreatePending_LoggedAtShouldBeAfterOccurredOn()
    {
        // Arrange
        var occurredOn = DateTime.UtcNow.AddDays(-1);

        // Act
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", occurredOn);

        // Assert
        log.LoggedAt.ShouldBeGreaterThan(log.OccurredOn);
    }

    [Fact]
    public void MarkAsPublished_ProcessedAtShouldBeAfterLoggedAt()
    {
        // Arrange
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow);

        // Act
        log.MarkAsPublished();

        // Assert
        log.ProcessedAt.ShouldNotBeNull();
        log.ProcessedAt!.Value.ShouldBeGreaterThanOrEqualTo(log.LoggedAt);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void CreatePending_WithEmptyContent_ShouldSucceed()
    {
        // Act
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "", DateTime.UtcNow);

        // Assert
        log.Content.ShouldBe("");
    }

    [Fact]
    public void CreatePending_WithLargeContent_ShouldSucceed()
    {
        // Arrange
        var largeContent = new string('x', 100000);

        // Act
        var log = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", largeContent, DateTime.UtcNow);

        // Assert
        log.Content.Length.ShouldBe(100000);
    }

    [Fact]
    public void CreatePending_ShouldGenerateUniqueIds()
    {
        // Act
        var log1 = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow);
        var log2 = IntegrationEventLog.CreatePending(Guid.NewGuid(), "TestEvent", "{}", DateTime.UtcNow);

        // Assert
        log1.Id.ShouldNotBe(log2.Id);
    }

    #endregion
}