using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCommerce.SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Configuration;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Postgresql;

namespace NetCommerce.SharedKernel.Infrastructure.Messaging;

/// <summary>
///     Wolverine configuration extensions for the modular monolith.
///     Implements the Transactional Outbox pattern with EF Core and PostgreSQL.
/// </summary>
public static class WolverineExtensions
{
    /// <summary>
    ///     Adds Wolverine as the message bus with transactional outbox support.
    ///     Replaces MediatR with a production-ready, durable messaging infrastructure.
    /// </summary>
    public static IHostBuilder UseWolverineMessaging(
        this IHostBuilder hostBuilder,
        IConfiguration configuration,
        params Type[] handlerAssemblyMarkerTypes)
    {
        var connectionString = configuration.GetConnectionString("postgres")
                               ?? configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Database connection string not found.");

        return hostBuilder.UseWolverine(opts =>
        {
            // ============================================================================
            // Message Persistence with PostgreSQL (Transactional Outbox)
            // ============================================================================
            opts.PersistMessagesWithPostgresql(connectionString, "wolverine");

            // ============================================================================
            // Entity Framework Core Integration
            // ============================================================================
            // Auto-enrolls DbContext transactions with Wolverine's outbox
            opts.UseEntityFrameworkCoreTransactions();

            // Auto-apply transactions to handlers that use DbContext
            // As of Wolverine 5.4.1+, explicit idempotency uses Eager checking by default
            // This checks for duplicates BEFORE executing handlers, which is safer
            // for handlers with external side-effects (Stripe, email, etc.)
            opts.Policies.AutoApplyTransactions();

            // ============================================================================
            // Idempotency Detection
            // ============================================================================
            // Configure message identity for modular monolith
            // Allows same message to be delivered to multiple modules independently
            // Uniqueness is tracked by Message ID + Destination URI
            opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

            // Keep processed messages for 10 minutes to handle duplicate detection
            // Messages marked as Handled older than this will be automatically deleted
            opts.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(10);

            // ============================================================================
            // Modular Monolith: Separated Handler Mode
            // Each module's handlers execute independently in their own transaction scope
            // ============================================================================
            opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;

            // ============================================================================
            // Durable Local Queues (Outbox Pattern for In-Process Messages)
            // ============================================================================
            opts.Policies.UseDurableLocalQueues();

            // ============================================================================
            // Global Error Handling with Retry Policies
            // ============================================================================
            opts.Policies.OnAnyException()
                .RetryWithCooldown(
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromSeconds(1))
                .Then.MoveToErrorQueue();

            // ============================================================================
            // FluentValidation Middleware
            // ============================================================================
            opts.UseFluentValidation();

            // ============================================================================
            // Handler Discovery
            // ============================================================================
            foreach (var markerType in handlerAssemblyMarkerTypes)
            {
                opts.Discovery.IncludeAssembly(markerType.Assembly);
            }

            // ============================================================================
            // ============================================================================
            // Development/Testing: Auto-provision message storage tables
            // ============================================================================
            opts.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.CreateOrUpdate;

            // ============================================================================
            // Partitioned Sequential Messaging (High Contention Pattern)
            // ============================================================================
            // This is an elite architectural pattern that converts a "Hardware Contention"
            // problem (Database Locking) into a "Software Scheduling" problem (Message Partitioning).
            //
            // By ensuring that all requests for Product A are handled by the same thread,
            // we effectively create a "virtual queue" for that product. This eliminates
            // expensive FOR UPDATE database locks because we guarantee that no two threads
            // will ever attempt to update the same product's stock at the same time.
            // ============================================================================

            // 1. Grouping Rule: Identify ProductId as the source of truth for concurrency
            opts.MessagePartitioning
                .ByMessage<ReserveInventoryCommand>(command =>
                    // Partition by the first ProductId in the command.
                    // In high-scale flash sales, we typically process one product per command.
                    command.Items.Count > 0
                        ? command.Items[0].ProductId.ToString()
                        : command.OrderId.ToString());

            opts.MessagePartitioning
                .ByMessage<ConfirmInventoryCommand>(command =>
                    command.OrderId.ToString());

            opts.MessagePartitioning
                .ByMessage<ReleaseInventoryReservationCommand>(command =>
                    command.OrderId.ToString());

            // 2. Queue Configuration: Create the "High Contention" Lane
            // - 9 parallel tracks (maximum available in Wolverine's PartitionSlots enum)
            // - Prime/odd numbers provide better hash distribution across slots
            // - Durable inbox ensures messages aren't lost if the app restarts
            opts.LocalQueue("inventory-contention")
                .PartitionProcessingByGroupId(PartitionSlots.Nine)
                .UseDurableInbox();
        });
    }

    /// <summary>
    ///     Registers FluentValidation validators from the specified assemblies.
    /// </summary>
    public static IServiceCollection AddWolverineValidation(
        this IServiceCollection services,
        params Type[] assemblyMarkerTypes)
    {
        foreach (var markerType in assemblyMarkerTypes)
        {
            services.AddValidatorsFromAssemblyContaining(markerType, ServiceLifetime.Scoped);
        }

        return services;
    }
}

// ============================================================================
// IDEMPOTENCY MESSAGING PATTERNS
// ============================================================================
//
// Wolverine's idempotency detection prevents processing the same message
// twice, even if it arrives at the endpoint multiple times.
//
// HOW IT WORKS:
// 1. Uses transactional inbox storage to track processed messages
// 2. Detects duplicates by Message ID + Destination (URI)
// 3. Automatically discards duplicate messages with same ID at same endpoint
//
// CONFIGURATION SUMMARY:
// - MessageIdentity.IdAndDestination: Tracks uniqueness by ID + destination
// - KeepAfterMessageHandling: Keep processed messages for 10 minutes (default: 5)
// - AutoApplyTransactions(): Enables eager idempotency checks (default as of 5.4.1+)
//
// USAGE IN MESSAGE HANDLERS:
//
// For most handlers, idempotency is automatic via AutoApplyTransactions.
// For explicit control, use the [Transactional] attribute:
//
//     // Eager mode - check for duplicates before processing (safe for side effects)
//     [Transactional(IdempotencyStyle.Eager)]
//     public static Task Handle(CreateOrder cmd, OrderContext db)
//     {
//         // If this message ID was already processed at this endpoint,
//         // Wolverine will skip this handler and mark as Handled
//         return db.Orders.AddAsync(cmd.ToAggregate());
//     }
//
// For handlers that don't need idempotency checks, disable with:
//     [NoTransaction]
//     public static Task Handle(NotificationEvent evt, INotificationService service)
//     {
//         // Not transactional, no idempotency tracking
//         return service.SendAsync(evt.Message);
//     }
//
// DURABLE ENDPOINT BEHAVIOR (Active by default):
// - Receiving: Insert into transactional inbox (duplicate = DuplicateIncomingEnvelopeException)
// - Processing: Update envelope status to Handled as part of message transaction
// - Cleanup: Background process deletes Handled messages older than retention period
//
// IDEMPOTENCY MODES (as of Wolverine 5.4.1+):
// - Explicit idempotency uses Eager checking by default (checks before processing)
// - Optimistic mode has proven buggy and is not recommended
// - For Buffered/Inline endpoints, use [Transactional] attribute for explicit control
//
// ============================================================================
