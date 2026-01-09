#nullable enable
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Audit;
using AuditCommand = NetCommerce.Kernel.Compliance.Audit.IAuditableCommand;
using AuditRepository = NetCommerce.Kernel.Compliance.Audit.IAuditRepository;
using AuditContext = NetCommerce.Kernel.Application.IUserContext;
using AuditService = NetCommerce.Kernel.Compliance.Audit.AuditService;
using Wolverine;

namespace NetCommerce.Kernel.Wolverine.Middleware;

/// <summary>
///     Wolverine Middleware for Automatic Audit Logging.
///     Runs BEFORE the command handler, capturing the audit entry even if handler fails.
/// </summary>
public static class AuditMiddleware
{
    /// <summary>
    ///     Wolverine "Before" middleware: Runs automatically before any IAuditableCommand handler.
    /// </summary>
    public static async Task Before(
        AuditCommand command,
        Envelope envelope,
        AuditContext userContext,
        AuditRepository auditRepository)
    {
        try
        {
            var auditService = new AuditService(auditRepository, userContext);
            await auditService.AuditAsync(command, envelope.CorrelationId);
        }
        catch (Exception ex)
        {
            // For financial systems: THROW (can't execute trades without audit trail)
            // For e-commerce: LOG and continue (don't block customer orders)
            throw new InvalidOperationException(
                $"Critical: Audit logging failed for {command.GetType().Name}. " +
                $"This command will not be executed for compliance reasons.", ex);
        }
    }
}
