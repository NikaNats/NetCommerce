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
    /// Takes only Envelope (no message parameter) so static codegen never needs to
    /// bind an IAuditableCommand variable: JasperFx resolves only the exact message
    /// type per chain, so both interface-typed and generic message parameters fail
    /// 'codegen write' with UnResolvableVariableException. The auditable command is
    /// recovered at runtime via Envelope.Message with an early return for safety.
    /// </summary>
    /// <param name="envelope">The wolverine metadata wrapper (carries the message)</param>
    /// <param name="userContext">Injected user context</param>
    /// <param name="auditRepository">Injected audit repository</param>
    /// <param name="logger">Standard ILogger</param>
    public static async Task Before(
        Envelope envelope,
        IUserContext userContext,
        IAuditRepository auditRepository,
        ILogger<AuditEntry> logger)
    {
        if (envelope.Message is not AuditCommand command)
            return;

        try
        {
            var auditService = new AuditService(auditRepository, userContext);
            // Use the built-in CorrelationId from the envelope
            await auditService.AuditAsync(command, envelope.CorrelationId ?? Guid.NewGuid().ToString());
        }
        catch (Exception ex)
        {
            // For legal compliance, we fail the operation if auditing fails
            logger.LogError(ex, "Audit logging failed for {MessageType}", command.GetType().Name);
            throw;
        }
    }
}
