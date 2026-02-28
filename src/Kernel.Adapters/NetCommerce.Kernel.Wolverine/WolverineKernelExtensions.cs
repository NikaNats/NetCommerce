#nullable enable
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Wolverine.Middleware;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence;

namespace NetCommerce.Kernel.Wolverine;

/// <summary>
///     Wolverine configuration extensions for the kernel.
/// </summary>
public static class WolverineKernelExtensions
{
    /// <summary>
    /// Configures the 'Production-Ready' Wolverine stack with a Transactional Outbox,
    /// Durable Inbox, and Multi-tenant Idempotency.
    /// </summary>
    public static WolverineOptions ConfigureKernelDefaults<TDbContext>(this WolverineOptions opts)
        where TDbContext : DbContext
    {
        // 1. Transactional Integrity
        // Ensures that your DB changes and your outgoing messages
        // (events) succeed or fail as a single atomic unit.
        opts.UseEntityFrameworkCoreTransactions();

        // 2. Durability & Idempotency (Docs Integration)
        // Set identity to include Destination. Vital for "Modular Monoliths"
        // where multiple handlers might receive the same message ID.
        opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

        // Ensure all local and external listeners use the persistent inbox
        opts.Policies.UseDurableInboxOnAllListeners();
        opts.Policies.UseDurableLocalQueues();

        // 3. Dead Letter Management (The "Final Mile")
        // Prevents PostgreSQL database from bloating over years of transient failures.
        // Financial audit trail requirement: 30 days retention.
        opts.Durability.DeadLetterQueueExpirationEnabled = true;
        opts.Durability.DeadLetterQueueExpiration = TimeSpan.FromDays(30);

        // 4. Middleware Application
        // Wolverine is smart enough to only apply AuditMiddleware
        // to messages implementing IAuditableCommand.
        opts.Policies.AddMiddleware(typeof(AuditMiddleware));

        // 5. Pure Canonical JSON Serialization (Phase 6 Complete)
        // All legacy SharedKernel types have been purged from the database.
        // JSON serialization uses standard System.Text.Json with default settings.
        // The API layer configures Source Generation via ConfigureHttpJsonOptions.
        opts.UseSystemTextJsonForSerialization(options =>
        {
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.WriteIndented = false;
            // Note: TypeInfoResolver is configured at the API layer via ConfigureHttpJsonOptions
            // to avoid circular dependencies between Kernel.Wolverine and Api projects.
        });

        return opts;
    }

    /// <summary>
    /// Development optimization to bypass leader election delays during debugging.
    /// </summary>
    public static WolverineOptions ConfigureDevelopmentMode(this WolverineOptions opts)
    {
        opts.Durability.Mode = DurabilityMode.Solo; // Fast cold-starts for developers
        return opts;
    }

    /// <summary>
    ///     Configures kernel middlewares for audit logging and request logging.
    /// </summary>
    public static WolverineOptions ConfigureKernelMiddlewares(this WolverineOptions opts)
    {
        opts.Policies.AddMiddleware(typeof(AuditMiddleware));
        opts.Policies.AddMiddleware(typeof(LoggingMiddleware));

        return opts;
    }

    /// <summary>
    ///     Configures only the audit middleware.
    /// </summary>
    public static WolverineOptions ConfigureAuditMiddleware(this WolverineOptions opts)
    {
        opts.Policies.AddMiddleware(typeof(AuditMiddleware));
        return opts;
    }

    /// <summary>
    ///     Configures only the logging middleware.
    /// </summary>
    public static WolverineOptions ConfigureLoggingMiddleware(this WolverineOptions opts)
    {
        opts.Policies.AddMiddleware(typeof(LoggingMiddleware));
        return opts;
    }

    /// <summary>
    ///     Configures Wolverine Transactional Outbox with EF Core.
    ///     Ensures messages are persisted atomically with database changes.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type.</typeparam>
    public static WolverineOptions ConfigureTransactionalOutbox<TDbContext>(
        this WolverineOptions opts)
        where TDbContext : DbContext
    {
        opts.UseEntityFrameworkCoreTransactions();

        return opts;
    }
}
