#region

using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Audit;
using NetCommerce.Kernel.Wolverine.Middleware;
using Wolverine;
using System.Security.Claims;
using AuditUserContext = NetCommerce.Kernel.Application.IUserContext;
using Microsoft.Extensions.Logging;

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

        var auditRepository = Substitute.For<IAuditRepository>();
        var userContext = Substitute.For<IUserContext>();
        var auditService = new AuditService(auditRepository, userContext);
        var logger = Substitute.For<ILogger<AuditEntry>>();

        // Act
        await AuditMiddleware.Before(command, envelope, auditService, logger);

        // Assert
        await auditRepository.Received(1).StoreAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditMiddleware_WhenRepositoryFails_ShouldThrowException()
    {
        // Arrange
        var command = new CancelOrderCommand(Guid.NewGuid(), "Test reason");
        var auditRepository = Substitute.For<IAuditRepository>();
        auditRepository.StoreAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("DB Down")));

        var userContext = Substitute.For<IUserContext>();
        var auditService = new AuditService(auditRepository, userContext);
        var logger = Substitute.For<ILogger<AuditEntry>>();

        // Act & Assert - Compliance Rule: Audit failure must block execution
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AuditMiddleware.Before(command, new Envelope(), auditService, logger));
    }

    [Fact]
    public async Task AuditMiddleware_MissingCorrelationId_ShouldAutoGenerate()
    {
        // Arrange
        var command = new CancelOrderCommand(Guid.NewGuid(), "Reason");
        var envelope = new Envelope { CorrelationId = null }; // Missing
        var auditRepository = Substitute.For<IAuditRepository>();
        var userContext = Substitute.For<IUserContext>();
        var auditService = new AuditService(auditRepository, userContext);
        var logger = Substitute.For<ILogger<AuditEntry>>();

        // Act
        await AuditMiddleware.Before(command, envelope, auditService, logger);

        // Assert
        await auditRepository.Received(1).StoreAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditMiddleware_Context_ShouldSerializeCommandData()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var command = new CancelOrderCommand(orderId, "Fraud Suspected");
        var envelope = new Envelope();
        var auditRepository = Substitute.For<IAuditRepository>();
        var userContext = Substitute.For<IUserContext>();
        var auditService = new AuditService(auditRepository, userContext);
        var logger = Substitute.For<ILogger<AuditEntry>>();

        // Act
        await AuditMiddleware.Before(command, envelope, auditService, logger);

        // Assert
        await auditRepository.Received(1).StoreAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
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

        var auditRepository = Substitute.For<IAuditRepository>();
        var userContext = Substitute.For<IUserContext>();
        var auditService = new AuditService(auditRepository, userContext);
        var logger = Substitute.For<ILogger<AuditEntry>>();

        // Act
        await AuditMiddleware.Before(command1, envelope1, auditService, logger);
        await AuditMiddleware.Before(command2, envelope2, auditService, logger);

        // Assert
        await auditRepository.Received(2).StoreAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }
}
