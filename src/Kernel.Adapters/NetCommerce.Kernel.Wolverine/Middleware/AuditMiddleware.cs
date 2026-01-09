#nullable enable
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Audit;
using AuditCommand = NetCommerce.Kernel.Compliance.Audit.IAuditableCommand;
using AuditService = NetCommerce.Kernel.Compliance.Audit.AuditService;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace NetCommerce.Kernel.Wolverine.Middleware;

/// <summary>
/// 2025 Elite Pattern: High-performance Audit Middleware.
/// Utilizes Wolverine's Method Injection to avoid manual 'new' operators.
/// </summary>
public static class AuditMiddleware
{
    /// <summary>
    /// Wolverine 'Before' middleware: Runs automatically for any IAuditableCommand.
    /// </summary>
    /// <param name="command">The message being handled</param>
    /// <param name="envelope">The wolverine metadata wrapper</param>
    /// <param name="auditService">Injected automatically from the Scoped container</param>
    /// <param name="logger">Standard ILogger</param>
    public static async Task Before(
        AuditCommand command,
        Envelope envelope,
        AuditService auditService,
        ILogger<AuditEntry> logger)
    {
        try
        {
            // The correlation ID is natively tracked by Wolverine
            var correlationId = envelope.CorrelationId ?? Guid.NewGuid().ToString();

            await auditService.AuditAsync(command, correlationId);
        }
        catch (Exception ex)
        {
            // CRITICAL: Decide on the 'Railway' failure strategy.
            // For legal compliance, we fail the entire handler if the audit log fails.
            logger.LogError(ex, "Compliance failure: Audit logging failed for {MessageType}", command.GetType().Name);
            throw;
        }
    }
}
