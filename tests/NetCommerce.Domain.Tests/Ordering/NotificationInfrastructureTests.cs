#nullable enable
#region

using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Infrastructure.Notifications;

#endregion

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Tests for InMemoryEmailProvider and SimpleTemplateEngine.
///     Ensures notification infrastructure works correctly.
/// </summary>
public class NotificationInfrastructureTests
{
    #region InMemoryEmailProvider Tests

    [Fact]
    public async Task InMemoryEmailProvider_SendEmail_ShouldStoreInMemory()
    {
        // Arrange
        ILogger<InMemoryEmailProvider>? logger = Substitute.For<ILogger<InMemoryEmailProvider>>();
        var provider = new InMemoryEmailProvider(logger);

        // Act
        await provider.SendEmailAsync(
            "test@example.com",
            "Test Subject",
            "<html><body>Test Body</body></html>",
            CancellationToken.None);

        // Assert
        IReadOnlyCollection<SentEmail> sentEmails = provider.GetSentEmails();
        sentEmails.ShouldHaveSingleItem();

        SentEmail email = sentEmails.First();
        email.To.ShouldBe("test@example.com");
        email.Subject.ShouldBe("Test Subject");
        email.HtmlBody.ShouldContain("Test Body");
        email.SentAt.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddSeconds(-5),
            DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task InMemoryEmailProvider_MultipleSends_ShouldStoreAll()
    {
        // Arrange
        ILogger<InMemoryEmailProvider>? logger = Substitute.For<ILogger<InMemoryEmailProvider>>();
        var provider = new InMemoryEmailProvider(logger);

        // Act
        await provider.SendEmailAsync("user1@test.com", "Subject 1", "Body 1");
        await provider.SendEmailAsync("user2@test.com", "Subject 2", "Body 2");
        await provider.SendEmailAsync("user3@test.com", "Subject 3", "Body 3");

        // Assert
        IReadOnlyCollection<SentEmail> sentEmails = provider.GetSentEmails();
        sentEmails.Count.ShouldBe(3);
        sentEmails.Select(e => e.To).ShouldContain("user1@test.com");
        sentEmails.Select(e => e.To).ShouldContain("user2@test.com");
        sentEmails.Select(e => e.To).ShouldContain("user3@test.com");
    }

    [Fact]
    public async Task InMemoryEmailProvider_Clear_ShouldRemoveAllEmails()
    {
        // Arrange
        ILogger<InMemoryEmailProvider>? logger = Substitute.For<ILogger<InMemoryEmailProvider>>();
        var provider = new InMemoryEmailProvider(logger);

        await provider.SendEmailAsync("test@example.com", "Subject", "Body");
        provider.GetSentEmails().ShouldHaveSingleItem();

        // Act
        provider.Clear();

        // Assert
        provider.GetSentEmails().ShouldBeEmpty();
    }

    [Fact]
    public async Task InMemoryEmailProvider_ThreadSafe_ShouldHandleConcurrentSends()
    {
        // Arrange
        ILogger<InMemoryEmailProvider>? logger = Substitute.For<ILogger<InMemoryEmailProvider>>();
        var provider = new InMemoryEmailProvider(logger);

        // Act - Send 100 emails concurrently
        Task[] tasks = Enumerable.Range(1, 100)
            .Select(i => provider.SendEmailAsync($"user{i}@test.com", $"Subject {i}", $"Body {i}"))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        IReadOnlyCollection<SentEmail> sentEmails = provider.GetSentEmails();
        sentEmails.Count.ShouldBe(100);
        sentEmails.Select(e => e.To).Distinct().Count().ShouldBe(100);
    }

    #endregion

    #region SimpleTemplateEngine Tests

    [Fact]
    public async Task SimpleTemplateEngine_OrderConfirmation_ShouldRenderCorrectly()
    {
        // Arrange
        var engine = new SimpleTemplateEngine();
        var model = new
        {
            CustomerName = "Jane Smith",
            OrderNumber = "ORD-2026-123",
            OrderId = Guid.NewGuid(),
            TotalAmount = 299.99m,
            Currency = "GEL"
        };

        // Act
        string html = await engine.RenderAsync("OrderConfirmation", model);

        // Assert
        html.ShouldContain("Jane Smith");
        html.ShouldContain("ORD-2026-123");
        html.ShouldContain(model.OrderId.ToString());
        html.ShouldContain("299.99");
        html.ShouldContain("GEL");
        html.ShouldContain("<!DOCTYPE html>");
        html.ShouldContain("Thank you for your order");
        html.ShouldContain("has been confirmed");
    }

    [Fact]
    public async Task SimpleTemplateEngine_OrderConfirmation_ShouldIncludeAllRequiredElements()
    {
        // Arrange
        var engine = new SimpleTemplateEngine();
        var model = new
        {
            CustomerName = "Test User",
            OrderNumber = "ORD-001",
            OrderId = Guid.NewGuid(),
            TotalAmount = 100m,
            Currency = "USD"
        };

        // Act
        string html = await engine.RenderAsync("OrderConfirmation", model);

        // Assert - Verify HTML structure
        html.ShouldContain("<html>");
        html.ShouldContain("</html>");
        html.ShouldContain("<head>");
        html.ShouldContain("</head>");
        html.ShouldContain("<body");
        html.ShouldContain("</body>");

        // Verify content sections
        html.ShouldContain("<h1>");
        html.ShouldContain("Order ID:");
        html.ShouldContain("Order Number:");
        html.ShouldContain("Total Amount:");
    }

    [Fact]
    public async Task SimpleTemplateEngine_UnknownTemplate_ShouldThrowException()
    {
        // Arrange
        var engine = new SimpleTemplateEngine();
        var model = new { Data = "test" };

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(() =>
            engine.RenderAsync("NonExistentTemplate", model));
    }

    [Fact]
    public async Task SimpleTemplateEngine_OrderConfirmation_ShouldFormatAmountCorrectly()
    {
        // Arrange
        var engine = new SimpleTemplateEngine();
        var model = new
        {
            CustomerName = "Test",
            OrderNumber = "ORD-002",
            OrderId = Guid.NewGuid(),
            TotalAmount = 1234.56m,
            Currency = "EUR"
        };

        // Act
        string html = await engine.RenderAsync("OrderConfirmation", model);

        // Assert - Check decimal formatting
        html.ShouldContain("1,234.56"); // Formatted with thousands separator
        html.ShouldContain("EUR");
    }

    [Theory]
    [InlineData("John", "ORD-100", 50.00, "GEL")]
    [InlineData("Alice", "ORD-200", 999.99, "USD")]
    [InlineData("Bob", "ORD-300", 0.01, "EUR")]
    public async Task SimpleTemplateEngine_OrderConfirmation_ShouldHandleVariousInputs(
        string name, string orderNumber, decimal amount, string currency)
    {
        // Arrange
        var engine = new SimpleTemplateEngine();
        var model = new
        {
            CustomerName = name,
            OrderNumber = orderNumber,
            OrderId = Guid.NewGuid(),
            TotalAmount = amount,
            Currency = currency
        };

        // Act
        string html = await engine.RenderAsync("OrderConfirmation", model);

        // Assert
        html.ShouldContain(name);
        html.ShouldContain(orderNumber);
        html.ShouldContain(currency);
        html.ShouldNotBeEmpty();
    }

    #endregion
}
