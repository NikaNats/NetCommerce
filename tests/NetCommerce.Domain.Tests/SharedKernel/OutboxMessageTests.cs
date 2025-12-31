using Shouldly;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

namespace NetCommerce.Domain.Tests.SharedKernel;

/// <summary>
/// Unit tests for OutboxMessage entity.
/// </summary>
public class OutboxMessageTests
{
    private readonly DateTime _testOccurredOn = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    #region Create Tests

    [Fact]
    public void Create_WithValidData_ShouldCreateOutboxMessage()
    {
        // Arrange
        const string type = "NetCommerce.Domain.Events.OrderCreatedEvent, NetCommerce.Domain";
        const string content = """{"orderId":"123","customerId":"456"}""";

        // Act
        var message = OutboxMessage.Create(type, content, _testOccurredOn);

        // Assert
        message.Id.ShouldNotBe(Guid.Empty);
        message.Type.ShouldBe(type);
        message.Content.ShouldBe(content);
        message.OccurredOn.ShouldBe(_testOccurredOn);
        message.ProcessedOn.ShouldBeNull();
        message.Error.ShouldBeNull();
        message.RetryCount.ShouldBe(0);
    }

    [Fact]
    public void Create_ShouldGenerateUniqueIds()
    {
        // Arrange
        const string type = "TestType";
        const string content = "{}";

        // Act
        var message1 = OutboxMessage.Create(type, content, _testOccurredOn);
        var message2 = OutboxMessage.Create(type, content, _testOccurredOn);

        // Assert
        message1.Id.ShouldNotBe(message2.Id);
    }

    #endregion

    #region MarkAsProcessed Tests

    [Fact]
    public void MarkAsProcessed_ShouldSetProcessedOnToCurrentUtcTime()
    {
        // Arrange
        var message = OutboxMessage.Create("TestType", "{}", _testOccurredOn);
        var beforeMark = DateTime.UtcNow;

        // Act
        message.MarkAsProcessed();
        var afterMark = DateTime.UtcNow;

        // Assert
        message.ProcessedOn.ShouldNotBeNull();
        message.ProcessedOn.Value.ShouldBeGreaterThanOrEqualTo(beforeMark);
        message.ProcessedOn.Value.ShouldBeLessThanOrEqualTo(afterMark);
    }

    [Fact]
    public void MarkAsProcessed_ShouldClearError()
    {
        // Arrange
        var message = OutboxMessage.Create("TestType", "{}", _testOccurredOn);
        message.MarkAsFailed("Some error");

        // Act
        message.MarkAsProcessed();

        // Assert
        message.Error.ShouldBeNull();
        message.ProcessedOn.ShouldNotBeNull();
    }

    #endregion

    #region MarkAsFailed Tests

    [Fact]
    public void MarkAsFailed_ShouldSetErrorAndIncrementRetryCount()
    {
        // Arrange
        var message = OutboxMessage.Create("TestType", "{}", _testOccurredOn);
        const string errorMessage = "Connection timeout";

        // Act
        message.MarkAsFailed(errorMessage);

        // Assert
        message.Error.ShouldBe(errorMessage);
        message.RetryCount.ShouldBe(1);
        message.ProcessedOn.ShouldBeNull();
    }

    [Fact]
    public void MarkAsFailed_MultipleTimes_ShouldIncrementRetryCount()
    {
        // Arrange
        var message = OutboxMessage.Create("TestType", "{}", _testOccurredOn);

        // Act
        message.MarkAsFailed("Error 1");
        message.MarkAsFailed("Error 2");
        message.MarkAsFailed("Error 3");

        // Assert
        message.RetryCount.ShouldBe(3);
        message.Error.ShouldBe("Error 3"); // Last error
    }

    #endregion

    #region CanRetry Tests

    [Theory]
    [InlineData(0, 3, true)]
    [InlineData(1, 3, true)]
    [InlineData(2, 3, true)]
    [InlineData(3, 3, false)]
    [InlineData(4, 3, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, false)]
    public void CanRetry_ShouldReturnCorrectValue(int retryCount, int maxRetries, bool expectedCanRetry)
    {
        // Arrange
        var message = OutboxMessage.Create("TestType", "{}", _testOccurredOn);
        for (var i = 0; i < retryCount; i++)
        {
            message.MarkAsFailed($"Error {i + 1}");
        }

        // Act
        var canRetry = message.CanRetry(maxRetries);

        // Assert
        canRetry.ShouldBe(expectedCanRetry);
    }

    #endregion

    #region State Transitions Tests

    [Fact]
    public void MessageLifecycle_FailThenSucceed_ShouldWorkCorrectly()
    {
        // Arrange
        var message = OutboxMessage.Create("TestType", "{}", _testOccurredOn);

        // Act - First attempt fails
        message.MarkAsFailed("Network error");
        
        // Assert intermediate state
        message.RetryCount.ShouldBe(1);
        message.CanRetry(3).ShouldBeTrue();
        message.ProcessedOn.ShouldBeNull();

        // Act - Second attempt succeeds
        message.MarkAsProcessed();

        // Assert final state
        message.ProcessedOn.ShouldNotBeNull();
        message.Error.ShouldBeNull();
        message.RetryCount.ShouldBe(1); // Retry count is NOT reset
    }

    [Fact]
    public void MessageLifecycle_ExhaustRetries_ShouldNotAllowMoreRetries()
    {
        // Arrange
        var message = OutboxMessage.Create("TestType", "{}", _testOccurredOn);
        const int maxRetries = 3;

        // Act - Exhaust all retries
        for (var i = 0; i < maxRetries; i++)
        {
            message.MarkAsFailed($"Error {i + 1}");
        }

        // Assert
        message.RetryCount.ShouldBe(maxRetries);
        message.CanRetry(maxRetries).ShouldBeFalse();
        message.ProcessedOn.ShouldBeNull();
    }

    #endregion
}
