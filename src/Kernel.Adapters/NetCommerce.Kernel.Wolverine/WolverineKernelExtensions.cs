#nullable enable
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Wolverine.Middleware;
using NetCommerce.Kernel.Wolverine.Serialization;
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

        // 5. Legacy Type Serialization (Phase 5/6 Migration Support)
        // CRITICAL: Register JSON converters for legacy SharedKernel types.
        // This ensures in-flight sagas and outbox messages can be deserialized
        // after the namespace migration from SharedKernel to Domain.Shared.
        // See docs/PHASE_5_SERIALIZATION_MIGRATION.md for timeline to remove.
        ConfigureLegacyTypeSerializationSupport(opts);

        return opts;
    }

    /// <summary>
    ///     Configures JSON serialization to handle legacy SharedKernel type names
    ///     during the Phase 5/6 namespace migration period.
    /// </summary>
    private static void ConfigureLegacyTypeSerializationSupport(WolverineOptions opts)
    {
        // ========================================================================
        // PHASE 5/6 MIGRATION: Two-Phase Type Resolution
        // ========================================================================
        // Wolverine resolves message types in two phases:
        //   1. ENVELOPE RESOLUTION: message_type column → Type (happens FIRST)
        //   2. JSON DESERIALIZATION: body column → object (happens SECOND)
        //
        // Both phases need to handle legacy type names from SharedKernel.
        // ========================================================================

        // PHASE 1: Register legacy message type aliases for envelope resolution.
        // Wolverine looks up the message_type string in HandlerGraph._messageTypes
        // BEFORE it attempts JSON deserialization. If this lookup fails, the message
        // goes directly to the Dead Letter Queue (DLQ).
        RegisterLegacyMessageTypeAliases(opts);

        // PHASE 2: Register JSON converters for saga state deserialization.
        // Value objects like Money and PriceBreakdown inside saga state may have
        // "$type" discriminators with legacy namespace names.
        opts.UseSystemTextJsonForSerialization(options =>
        {
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.WriteIndented = false;
            options.TypeInfoResolver = new LegacyTypeResolver();
            options.Converters.Add(new LegacyTypeConverter());
        });
    }

    /// <summary>
    ///     Registers legacy SharedKernel message type names as aliases.
    ///     This allows Wolverine to resolve messages in the outbox/inbox
    ///     that were persisted before the Phase 5/6 namespace migration.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     CRITICAL: This must be called BEFORE Wolverine starts processing messages.
    ///     Without this registration, messages with legacy type names will fail at
    ///     envelope resolution and be moved to the Dead Letter Queue.
    ///     </para>
    ///     <para>
    ///     Migration Timeline: Monitor <see cref="LegacyTypeResolver.LegacyResolutionCount"/>
    ///     for zero hits over a 30-60 day period. Once all legacy sagas complete,
    ///     this method can be removed.
    ///     </para>
    /// </remarks>
    private static void RegisterLegacyMessageTypeAliases(WolverineOptions opts)
    {
        // Integration Events - Order Lifecycle
        opts.RegisterMessageType(typeof(StartOrderFulfillmentCommand),
            "NetCommerce.SharedKernel.Events.StartOrderFulfillmentCommand");
        opts.RegisterMessageType(typeof(OrderSubmittedIntegrationEvent),
            "NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent");
        opts.RegisterMessageType(typeof(OrderGracePeriodConfirmedIntegrationEvent),
            "NetCommerce.SharedKernel.Events.OrderGracePeriodConfirmedIntegrationEvent");
        opts.RegisterMessageType(typeof(OrderPlacedIntegrationEvent),
            "NetCommerce.SharedKernel.Events.OrderPlacedIntegrationEvent");
        opts.RegisterMessageType(typeof(OrderCancelledIntegrationEvent),
            "NetCommerce.SharedKernel.Events.OrderCancelledIntegrationEvent");

        // Integration Events - Inventory
        opts.RegisterMessageType(typeof(ReserveInventoryCommand),
            "NetCommerce.SharedKernel.Events.ReserveInventoryCommand");
        opts.RegisterMessageType(typeof(InventoryReserved),
            "NetCommerce.SharedKernel.Events.InventoryReserved");
        opts.RegisterMessageType(typeof(InventoryReservationFailed),
            "NetCommerce.SharedKernel.Events.InventoryReservationFailed");
        opts.RegisterMessageType(typeof(LockInventoryForPaymentCommand),
            "NetCommerce.SharedKernel.Events.LockInventoryForPaymentCommand");
        opts.RegisterMessageType(typeof(InventoryLocked),
            "NetCommerce.SharedKernel.Events.InventoryLocked");
        opts.RegisterMessageType(typeof(ConfirmInventoryCommand),
            "NetCommerce.SharedKernel.Events.ConfirmInventoryCommand");
        opts.RegisterMessageType(typeof(InventoryConfirmed),
            "NetCommerce.SharedKernel.Events.InventoryConfirmed");
        opts.RegisterMessageType(typeof(InventoryConfirmationFailed),
            "NetCommerce.SharedKernel.Events.InventoryConfirmationFailed");
        opts.RegisterMessageType(typeof(ReleaseInventoryReservationCommand),
            "NetCommerce.SharedKernel.Events.ReleaseInventoryReservationCommand");

        // Integration Events - Payments
        opts.RegisterMessageType(typeof(RequestPaymentCommand),
            "NetCommerce.SharedKernel.Events.RequestPaymentCommand");
        opts.RegisterMessageType(typeof(PaymentInitiated),
            "NetCommerce.SharedKernel.Events.PaymentInitiated");
        opts.RegisterMessageType(typeof(PaymentSucceeded),
            "NetCommerce.SharedKernel.Events.PaymentSucceeded");
        opts.RegisterMessageType(typeof(PaymentFailed),
            "NetCommerce.SharedKernel.Events.PaymentFailed");
        opts.RegisterMessageType(typeof(RefundPaymentCommand),
            "NetCommerce.SharedKernel.Events.RefundPaymentCommand");
        opts.RegisterMessageType(typeof(RefundCompleted),
            "NetCommerce.SharedKernel.Events.RefundCompleted");
        opts.RegisterMessageType(typeof(RefundFailed),
            "NetCommerce.SharedKernel.Events.RefundFailed");

        // Saga Timeout Messages
        opts.RegisterMessageType(typeof(InventoryReservationTimeoutMessage),
            "NetCommerce.SharedKernel.Events.InventoryReservationTimeoutMessage");
        opts.RegisterMessageType(typeof(GracePeriodTimeout),
            "NetCommerce.SharedKernel.Events.GracePeriodTimeout");
        opts.RegisterMessageType(typeof(PaymentTimeoutMessage),
            "NetCommerce.SharedKernel.Events.PaymentTimeoutMessage");
        opts.RegisterMessageType(typeof(InventoryConfirmationTimeoutMessage),
            "NetCommerce.SharedKernel.Events.InventoryConfirmationTimeoutMessage");

        // Saga Terminal Commands
        opts.RegisterMessageType(typeof(FinalizeOrderCommand),
            "NetCommerce.SharedKernel.Events.FinalizeOrderCommand");
        opts.RegisterMessageType(typeof(FailOrderCommand),
            "NetCommerce.SharedKernel.Events.FailOrderCommand");
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
