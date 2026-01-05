#nullable enable

using System.Text.Json;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using Wolverine;

namespace NetCommerce.SharedKernel.Infrastructure.Messaging;

/// <summary>
///     2025 Elite Pattern: Wolverine Middleware for Automatic Audit Logging.
///     
///     How it works:
///     1. Wolverine detects any command implementing IAuditableCommand
///     2. BEFORE the handler executes, this middleware captures the audit entry
///     3. The audit entry is stored in the immutable ledger
///     4. The original command proceeds to its handler
///     
///     Why middleware instead of manual logging?
///     - Zero coupling: Business logic never calls audit code
///     - Guaranteed execution: Cannot be forgotten by developers
///     - Centralized policy: Change audit format in one place
///     - Cross-cutting concern: Separated from domain logic
///     
///     Security Note:
///     This runs BEFORE the command handler, so even if the handler fails or is denied,
///     the audit entry shows that someone ATTEMPTED the action.
/// </summary>
public static class AuditMiddleware
{
    /// <summary>
    ///     Wolverine "Before" middleware: Runs automatically before any IAuditableCommand handler.
    ///     
    ///     Method signature is special:
    ///     - Wolverine detects the "Before" name
    ///     - Parameters are injected from DI container
    ///     - The command is passed as the first parameter
    /// </summary>
    public static async Task Before(
        IAuditableCommand command,
        Envelope envelope,
        IUserContext userContext,
        IAuditRepository auditRepository)
    {
        try
        {
            // Extract the action name from the command type
            var actionName = command.GetType().Name
                .Replace("Command", string.Empty)
                .Replace("Query", string.Empty);

            // Serialize the command payload for the Context field
            // This captures the complete business intent (e.g., "Reason: Fraud suspect")
            // We serialize the actual command type, not the interface, to capture all properties
            var commandType = command.GetType();
            var contextJson = JsonSerializer.Serialize(command, commandType, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Create the immutable audit entry
            var auditEntry = AuditEntry.Create(
                userId: userContext.UserId,
                userRole: userContext.Role,
                action: $"{command.Module}.{actionName}",
                resourceId: command.GetResourceId(),
                module: command.Module,
                context: contextJson,
                correlationId: envelope.CorrelationId ?? Guid.NewGuid().ToString(),
                ipAddress: userContext.IpAddress,
                userAgent: userContext.UserAgent
            );

            // Store in the append-only ledger
            await auditRepository.StoreAsync(auditEntry);
        }
        catch (Exception ex)
        {
            // 2025 Elite Decision: Should audit failure block the command?
            // Option A: Throw (strict compliance - if audit fails, command fails)
            // Option B: Log and continue (availability over audit)
            // 
            // For financial systems: THROW (can't execute trades without audit trail)
            // For e-commerce: LOG and continue (don't block customer orders)
            // 
            // Here we throw to ensure no unaudited sensitive actions occur.
            throw new InvalidOperationException(
                $"Critical: Audit logging failed for {command.GetType().Name}. " +
                $"This command will not be executed for compliance reasons.", ex);
        }
    }

    /// <summary>
    ///     Optional: "After" middleware to log successful completion.
    ///     This can capture the RESULT of the command (e.g., "Order #12345 created").
    /// </summary>
    public static async Task After(
        IAuditableCommand command,
        object? result,
        Envelope envelope,
        IUserContext userContext,
        IAuditRepository auditRepository)
    {
        // You can optionally log a "Success" audit entry here
        // This creates a two-phase audit: "Attempted" (Before) + "Completed" (After)
        // Useful for tracking failed vs successful operations separately
        
        // For now, we only audit the INTENT (Before), not the outcome
        await Task.CompletedTask;
    }
}
