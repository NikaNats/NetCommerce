#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        // 3. Middleware Application
        // Wolverine is smart enough to only apply AuditMiddleware
        // to messages implementing IAuditableCommand.
        opts.Policies.AddMiddleware(typeof(AuditMiddleware));
        opts.Policies.AddMiddleware(typeof(LoggingMiddleware));

        // 4. Optimization: Sequential Handling per Tenant/Group
        // Prevents concurrency race conditions on the same entity.
        opts.MessagePartitioning.UseInferredMessageGrouping();

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
    [Obsolete("Use ConfigureKernelDefaults instead for production-ready configuration.")]
    public static WolverineOptions ConfigureKernelMiddlewares(this WolverineOptions opts)
    {
        opts.Policies.AddMiddleware(typeof(AuditMiddleware));
        opts.Policies.AddMiddleware(typeof(LoggingMiddleware));

        return opts;
    }

    /// <summary>
    ///     Configures only the audit middleware.
    /// </summary>
    [Obsolete("Use ConfigureKernelDefaults instead for production-ready configuration.")]
    public static WolverineOptions ConfigureAuditMiddleware(this WolverineOptions opts)
    {
        opts.Policies.AddMiddleware(typeof(AuditMiddleware));
        return opts;
    }

    /// <summary>
    ///     Configures only the logging middleware.
    /// </summary>
    [Obsolete("Use ConfigureLoggingMiddleware instead for production-ready configuration.")]
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
    [Obsolete("Use ConfigureKernelDefaults instead for production-ready configuration.")]
    public static WolverineOptions ConfigureTransactionalOutbox<TDbContext>(
        this WolverineOptions opts)
        where TDbContext : DbContext
    {
        opts.UseEntityFrameworkCoreTransactions();

        return opts;
    }
}
