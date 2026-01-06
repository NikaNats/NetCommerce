#region

using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Audit;
using NetCommerce.Kernel.Wolverine.Middleware;
using Wolverine;

#endregion

namespace NetCommerce.Domain.Tests.Auditing;

public class AuditMiddlewareTests
{
    [Fact]
    public async Task AuditMiddleware_CancelOrderCommand_ShouldCreateAuditEntry()
    {
        // Arrange
        var command = new CancelOrderCommand(Guid.NewGuid(), "Customer requested refund - Item not as described");
        var envelope = new Envelope { CorrelationId = "correlation_abc123" };

        IUserContext? userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("admin_xyz789");
        userContext.Role.Returns("Admin");
        userContext.IpAddress.Returns("192.168.1.100");
        userContext.UserAgent.Returns("Mozilla/5.0");

        IAuditRepository? auditRepository = Substitute.For<IAuditRepository>();
        AuditEntry? capturedEntry = null;
        await auditRepository.StoreAsync(Arg.Do<AuditEntry>(e => capturedEntry = e));

        // Act
        await AuditMiddleware.Before(command, envelope, userContext, auditRepository);

        // Assert
        capturedEntry.ShouldNotBeNull();
        capturedEntry.UserId.ShouldBe("admin_xyz789");
        capturedEntry.Action.ShouldBe("Ordering.CancelOrder");
        capturedEntry.CorrelationId.ShouldBe("correlation_abc123");
        capturedEntry.Context.ShouldContain("Customer requested refund");
    }

    [Fact]
    public async Task AuditMiddleware_WhenRepositoryFails_ShouldThrowException()
    {
        // Arrange
        var command = new CancelOrderCommand(Guid.NewGuid(), "Test reason");
        IAuditRepository? auditRepository = Substitute.For<IAuditRepository>();
        auditRepository.StoreAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("DB Down"));

        // Act & Assert - Compliance Rule: Audit failure must block execution
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AuditMiddleware.Before(command, new Envelope(), Substitute.For<IUserContext>(), auditRepository));
    }

    [Fact]
    public async Task AuditMiddleware_MissingCorrelationId_ShouldAutoGenerate()
    {
        // Arrange
        var command = new CancelOrderCommand(Guid.NewGuid(), "Reason");
        var envelope = new Envelope { CorrelationId = null }; // Missing
        IUserContext? userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("user123");
        IAuditRepository? repo = Substitute.For<IAuditRepository>();
        AuditEntry? entry = null;
        await repo.StoreAsync(Arg.Do<AuditEntry>(e => entry = e));

        // Act
        await AuditMiddleware.Before(command, envelope, userContext, repo);

        // Assert
        entry.ShouldNotBeNull();
        entry.CorrelationId.ShouldNotBeNullOrEmpty(); // Traceability preserved
    }

    [Fact]
    public async Task AuditMiddleware_Context_ShouldSerializeCommandData()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var command = new CancelOrderCommand(orderId, "Fraud Suspected");
        var envelope = new Envelope();
        IUserContext? userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("user123");
        IAuditRepository? repo = Substitute.For<IAuditRepository>();
        AuditEntry? entry = null;
        await repo.StoreAsync(Arg.Do<AuditEntry>(e => entry = e));

        // Act
        await AuditMiddleware.Before(command, envelope, userContext, repo);

        // Assert
        entry.Context.ShouldContain(orderId.ToString());
        entry.Context.ShouldContain("Fraud Suspected");
    }

    [Fact]
    public async Task AuditMiddleware_MultipleCommands_ShouldCreateSeparateEntries()
    {
        // Arrange
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var command1 = new CancelOrderCommand(orderId1, "Reason 1");
        var command2 = new CancelOrderCommand(orderId2, "Reason 2");

        var envelope1 = new Envelope { CorrelationId = "corr1" };
        var envelope2 = new Envelope { CorrelationId = "corr2" };

        IUserContext? userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns("admin123");
        userContext.Role.Returns("Admin");

        IAuditRepository? auditRepository = Substitute.For<IAuditRepository>();
        var capturedEntries = new List<AuditEntry>();
        await auditRepository.StoreAsync(Arg.Do<AuditEntry>(e => capturedEntries.Add(e)), Arg.Any<CancellationToken>());

        // Act
        await AuditMiddleware.Before(command1, envelope1, userContext, auditRepository);
        await AuditMiddleware.Before(command2, envelope2, userContext, auditRepository);

        // Assert
        capturedEntries.Count.ShouldBe(2);
        capturedEntries[0].ResourceId.ShouldBe(orderId1.ToString());
        capturedEntries[0].CorrelationId.ShouldBe("corr1");
        capturedEntries[0].Context.ShouldContain("Reason 1");

        capturedEntries[1].ResourceId.ShouldBe(orderId2.ToString());
        capturedEntries[1].CorrelationId.ShouldBe("corr2");
        capturedEntries[1].Context.ShouldContain("Reason 2");
    }
}
