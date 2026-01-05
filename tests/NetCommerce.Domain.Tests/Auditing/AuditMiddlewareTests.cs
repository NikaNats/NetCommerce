#nullable enable

using System.Text.Json;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Messaging;
using NSubstitute;
using Shouldly;
using Wolverine;

namespace NetCommerce.Domain.Tests.Auditing;

/// <summary>
///     2025 Elite Pattern: Tests for Automatic Audit Logging via Wolverine Middleware.
///     
///     What we're testing:
///     1. Audit entries are created automatically for IAuditableCommand
///     2. All required fields are populated correctly
///     3. Business intent is captured in Context (JSON)
///     4. Correlation with technical logs via CorrelationId
///     5. Failure handling (should audit logging failure block the command?)
/// </summary>
public class AuditMiddlewareTests
{
    [Fact]
    public async Task AuditMiddleware_CancelOrderCommand_ShouldCreateAuditEntry()
    {
        // Arrange
        var command = new CancelOrderCommand(
            OrderId: Guid.NewGuid(),
            Reason: "Customer requested refund - Item not as described");

        var envelope = new Envelope
        {
            CorrelationId = "correlation_abc123"
        };

        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("admin_xyz789");
        userContext.Role.Returns("Admin");
        userContext.IpAddress.Returns("192.168.1.100");
        userContext.UserAgent.Returns("Mozilla/5.0");

        var auditRepository = Substitute.For<IAuditRepository>();
        AuditEntry? capturedEntry = null;
        await auditRepository.StoreAsync(Arg.Do<AuditEntry>(e => capturedEntry = e));

        // Act
        await AuditMiddleware.Before(command, envelope, userContext, auditRepository);

        // Assert
        capturedEntry.ShouldNotBeNull();
        capturedEntry.UserId.ShouldBe("admin_xyz789");
        capturedEntry.UserRole.ShouldBe("Admin");
        capturedEntry.Action.ShouldBe("Ordering.CancelOrder");
        capturedEntry.ResourceId.ShouldBe(command.OrderId.ToString());
        capturedEntry.Module.ShouldBe("Ordering");
        capturedEntry.CorrelationId.ShouldBe("correlation_abc123");
        capturedEntry.IpAddress.ShouldBe("192.168.1.100");
        capturedEntry.UserAgent.ShouldBe("Mozilla/5.0");

        // Verify business intent is captured in Context
        capturedEntry.Context.ShouldContain("Customer requested refund");
        capturedEntry.Context.ShouldContain(command.OrderId.ToString());

        // Verify timestamp is recent (within last minute)
        capturedEntry.Timestamp.ShouldBeInRange(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task AuditMiddleware_WithMissingCorrelationId_ShouldGenerateOne()
    {
        // Arrange
        var command = new CancelOrderCommand(Guid.NewGuid(), "Test reason");
        var envelope = new Envelope { CorrelationId = null }; // Missing!
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("user_123");
        userContext.Role.Returns("Customer");

        var auditRepository = Substitute.For<IAuditRepository>();
        AuditEntry? capturedEntry = null;
        await auditRepository.StoreAsync(Arg.Do<AuditEntry>(e => capturedEntry = e));

        // Act
        await AuditMiddleware.Before(command, envelope, userContext, auditRepository);

        // Assert
        capturedEntry.ShouldNotBeNull();
        capturedEntry.CorrelationId.ShouldNotBeNullOrEmpty();
        Guid.TryParse(capturedEntry.CorrelationId, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task AuditMiddleware_WhenRepositoryFails_ShouldThrowException()
    {
        // Arrange - Simulate audit repository failure (DB down, disk full, etc.)
        var command = new CancelOrderCommand(Guid.NewGuid(), "Test reason");
        var envelope = new Envelope { CorrelationId = "test_123" };
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("user_123");
        userContext.Role.Returns("Admin");

        var auditRepository = Substitute.For<IAuditRepository>();
        auditRepository.StoreAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns<Task>(x => throw new InvalidOperationException("Database connection failed"));

        // Act & Assert
        // 2025 Elite Decision: Audit failure MUST block the command for compliance
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AuditMiddleware.Before(command, envelope, userContext, auditRepository));

        exception.Message.ShouldContain("Critical: Audit logging failed");
        exception.Message.ShouldContain("CancelOrderCommand");
        exception.InnerException.ShouldNotBeNull();
        exception.InnerException!.Message.ShouldContain("Database connection failed");
    }

    [Fact]
    public async Task AuditMiddleware_MultipleCommands_ShouldCreateSeparateEntries()
    {
        // Arrange
        var command1 = new CancelOrderCommand(Guid.NewGuid(), "Reason 1");
        var command2 = new CancelOrderCommand(Guid.NewGuid(), "Reason 2");
        var envelope1 = new Envelope { CorrelationId = "corr_1" };
        var envelope2 = new Envelope { CorrelationId = "corr_2" };

        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("user_123");
        userContext.Role.Returns("Admin");

        var auditRepository = Substitute.For<IAuditRepository>();
        var capturedEntries = new List<AuditEntry>();
        await auditRepository.StoreAsync(Arg.Do<AuditEntry>(e => capturedEntries.Add(e)));

        // Act
        await AuditMiddleware.Before(command1, envelope1, userContext, auditRepository);
        await AuditMiddleware.Before(command2, envelope2, userContext, auditRepository);

        // Assert
        capturedEntries.Count.ShouldBe(2);
        capturedEntries[0].ResourceId.ShouldBe(command1.OrderId.ToString());
        capturedEntries[0].CorrelationId.ShouldBe("corr_1");
        capturedEntries[1].ResourceId.ShouldBe(command2.OrderId.ToString());
        capturedEntries[1].CorrelationId.ShouldBe("corr_2");
    }

    [Fact]
    public async Task AuditMiddleware_ContextJson_ShouldBeParseable()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var command = new CancelOrderCommand(orderId, "Customer fraud alert");
        var envelope = new Envelope { CorrelationId = "test" };
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("admin_123");
        userContext.Role.Returns("Admin");

        var auditRepository = Substitute.For<IAuditRepository>();
        AuditEntry? capturedEntry = null;
        await auditRepository.StoreAsync(Arg.Do<AuditEntry>(e => capturedEntry = e));

        // Act
        await AuditMiddleware.Before(command, envelope, userContext, auditRepository);

        // Assert - Verify Context is valid JSON
        capturedEntry.ShouldNotBeNull();
        
        JsonDocument? jsonDoc = null;
        try
        {
            jsonDoc = JsonDocument.Parse(capturedEntry.Context);
        }
        catch (JsonException)
        {
            Assert.Fail("Context should be valid JSON");
        }

        jsonDoc.ShouldNotBeNull();

        // Verify it contains command properties in camelCase
        var root = jsonDoc.RootElement;
        root.GetProperty("orderId").GetGuid().ShouldBe(orderId);
        root.GetProperty("reason").GetString().ShouldBe("Customer fraud alert");
        root.GetProperty("module").GetString().ShouldBe("Ordering");
    }
}
