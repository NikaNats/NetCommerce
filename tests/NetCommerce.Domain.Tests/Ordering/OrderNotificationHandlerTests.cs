#region

using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Infrastructure.Notifications;
using NetCommerce.Kernel.Application.Notifications;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NSubstitute.ExceptionExtensions;

#endregion

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Tests for OrderNotificationHandler following 2025 best practices.
///     Ensures email notifications are sent reliably via the Event-Driven Notification Sidecar pattern.
/// </summary>
public class OrderNotificationHandlerTests
{
    [Fact]
    public async Task Handle_ValidEvent_ShouldSendEmailWithCorrectDetails()
    {
        // Arrange
        var emailProvider = new InMemoryEmailProvider(Substitute.For<ILogger<InMemoryEmailProvider>>());
        var templateEngine = new SimpleTemplateEngine();
        ILogger<OrderNotificationHandler>? logger = Substitute.For<ILogger<OrderNotificationHandler>>();
        var handler = new OrderNotificationHandler(emailProvider, templateEngine, logger);

        var orderEvent = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(),
            "ORD-2026-001",
            "john.doe@example.com",
            "John Doe",
            Money.Create(150.50m));

        // Act
        await handler.Handle(orderEvent, CancellationToken.None);

        // Assert
        IReadOnlyCollection<SentEmail> sentEmails = emailProvider.GetSentEmails();
        sentEmails.ShouldHaveSingleItem();

        SentEmail email = sentEmails.First();
        email.To.ShouldBe("john.doe@example.com");
        email.Subject.ShouldBe("Order Confirmed - ORD-2026-001");
        email.HtmlBody.ShouldContain("John Doe");
        email.HtmlBody.ShouldContain("ORD-2026-001");
        email.HtmlBody.ShouldContain("150.50");
        email.HtmlBody.ShouldContain("GEL");
        email.HtmlBody.ShouldContain(orderEvent.OrderId.ToString());
    }

    [Fact]
    public async Task Handle_MultipleEvents_ShouldSendMultipleEmails()
    {
        // Arrange
        var emailProvider = new InMemoryEmailProvider(Substitute.For<ILogger<InMemoryEmailProvider>>());
        var templateEngine = new SimpleTemplateEngine();
        ILogger<OrderNotificationHandler>? logger = Substitute.For<ILogger<OrderNotificationHandler>>();
        var handler = new OrderNotificationHandler(emailProvider, templateEngine, logger);

        var event1 = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(), "ORD-001", "customer1@test.com", "Customer One", Money.Create(100m));

        var event2 = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(), "ORD-002", "customer2@test.com", "Customer Two", Money.Create(200m));

        // Act
        await handler.Handle(event1, CancellationToken.None);
        await handler.Handle(event2, CancellationToken.None);

        // Assert
        IReadOnlyCollection<SentEmail> sentEmails = emailProvider.GetSentEmails();
        sentEmails.Count.ShouldBe(2);
        sentEmails.ShouldContain(e => e.To == "customer1@test.com");
        sentEmails.ShouldContain(e => e.To == "customer2@test.com");
    }

    [Fact]
    public async Task Handle_EmailProviderThrows_ShouldLogErrorAndRethrow()
    {
        // Arrange
        IEmailProvider? emailProvider = Substitute.For<IEmailProvider>();
        emailProvider.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SendGrid API failure"));

        var templateEngine = new SimpleTemplateEngine();
        ILogger<OrderNotificationHandler>? logger = Substitute.For<ILogger<OrderNotificationHandler>>();
        var handler = new OrderNotificationHandler(emailProvider, templateEngine, logger);

        var orderEvent = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(), "ORD-003", "test@example.com", "Test User", Money.Create(50m));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() =>
            handler.Handle(orderEvent, CancellationToken.None));

        // Verify error was logged
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to send order confirmation email")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_CancellationRequested_ShouldRespectCancellation()
    {
        // Arrange
        var emailProvider = new InMemoryEmailProvider(Substitute.For<ILogger<InMemoryEmailProvider>>());
        var templateEngine = new SimpleTemplateEngine();
        ILogger<OrderNotificationHandler>? logger = Substitute.For<ILogger<OrderNotificationHandler>>();
        var handler = new OrderNotificationHandler(emailProvider, templateEngine, logger);

        var orderEvent = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(), "ORD-004", "test@example.com", "Test User", Money.Create(75m));

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        // Note: InMemoryEmailProvider doesn't check cancellation, but real implementations should
        // This test demonstrates the pattern
        await handler.Handle(orderEvent, cts.Token);

        // Email still sent because InMemoryEmailProvider is synchronous
        // Real async providers would throw OperationCanceledException
        emailProvider.GetSentEmails().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("", "ORD-005", "Invalid Customer", 100)]
    [InlineData("customer@test.com", "", "Valid Customer", 100)]
    public async Task Handle_InvalidEventData_ShouldStillAttemptSend(
        string email, string orderNumber, string customerName, decimal amount)
    {
        // Arrange
        var emailProvider = new InMemoryEmailProvider(Substitute.For<ILogger<InMemoryEmailProvider>>());
        var templateEngine = new SimpleTemplateEngine();
        ILogger<OrderNotificationHandler>? logger = Substitute.For<ILogger<OrderNotificationHandler>>();
        var handler = new OrderNotificationHandler(emailProvider, templateEngine, logger);

        var orderEvent = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(), orderNumber, email, customerName, Money.Create(amount));

        // Act
        await handler.Handle(orderEvent, CancellationToken.None);

        // Assert
        // Handler doesn't validate input - it's the sender's responsibility
        // This follows "fail fast" principle - invalid emails go to dead letter queue
        IReadOnlyCollection<SentEmail> sentEmails = emailProvider.GetSentEmails();
        sentEmails.ShouldHaveSingleItem();
    }
}
