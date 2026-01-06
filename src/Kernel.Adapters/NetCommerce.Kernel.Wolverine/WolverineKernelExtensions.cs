#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Kernel.Wolverine.Middleware;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace NetCommerce.Kernel.Wolverine;

/// <summary>
///     Wolverine configuration extensions for the kernel.
/// </summary>
public static class WolverineKernelExtensions
{
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
