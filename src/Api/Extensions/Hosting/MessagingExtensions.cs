using JasperFx.CodeGeneration;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Kernel.EfCore.Persistence;
using NetCommerce.Kernel.Wolverine;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Payments.Application.Transactions.Commands;
using Wolverine;
using Wolverine.Http;
using Wolverine.Postgresql;
using Wolverine.RDBMS;
using Wolverine.Runtime;
using Wolverine.SignalR;

namespace NetCommerce.Api.Extensions.Hosting;

/// <summary>
///     Wolverine message bus with transactional outbox (replaces MediatR with durable,
///     at-least-once message delivery). Extracted verbatim from Program.cs — registration
///     order and discovery set are unchanged.
/// </summary>
public static class MessagingExtensions
{
    public static IHostBuilder AddEnterpriseWolverine(
        this IHostBuilder host,
        IConfiguration config,
        bool isTestOrDev)
    {
        var connectionString = config.GetConnectionString("OrderingDb")
            ?? config.GetConnectionString("postgres");

        host.UseWolverineMessaging(
            config,
            opts =>
            {
                // 1. Explicitly pin ApplicationAssembly to NetCommerce.Api so Wolverine always looks
                // for handlers in the API assembly, even when hosted via WebApplicationFactory (where
                // the test runner's entry assembly defaults to NetCommerce.Integration.Tests).
                // global::Program: top-level-statements entry point lives in the global namespace.
                opts.ApplicationAssembly = typeof(global::Program).Assembly;

                // 2. In Development and Testing environments, allow dynamic Auto discovery so WebApplicationFactory
                // and local test runners resolve handlers seamlessly.
                // In Production (Native AOT container), strictly enforce TypeLoadMode.Static.
                opts.CodeGeneration.TypeLoadMode = isTestOrDev ? TypeLoadMode.Auto : TypeLoadMode.Static;

                opts.CodeGeneration.GeneratedCodeOutputPath =
                    Path.Combine(Directory.GetCurrentDirectory(), "Internal", "Generated");

                if (!string.IsNullOrEmpty(connectionString))
                {
                    opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
                }

                opts.AddSagaType<OrderFulfillmentSaga>();
                opts.ConfigureKernelDefaults<BaseDbContext>();

                // -------------------------------------------------------------
                // Real-time order notifications over SignalR.
                //
                // Without BOTH of these the saga's OrderStatusChanged is unroutable:
                // it goes to the local queue and is dropped, while
                // MapWolverineSignalRHub("/api/messages") (PipelineExtensions.cs:135)
                // sits there as a dead endpoint. A browser client then connects
                // successfully and receives nothing — which is worse than a
                // visible failure, because the UI looks live.
                //
                // UseSignalR() registers the transport (verified present on the
                // pinned WolverineFx.SignalR 6.41.0 via reflection). It does NOT
                // itself require SignalR's services — verified by negative control:
                // a host with UseSignalR() but no AddSignalR() still delivered a
                // frame to a live @microsoft/signalr client 7/7. Program.cs calls
                // AddSignalR() because it also MAPS the hub endpoint, which does
                // need them.
                opts.UseSignalR();

                // Publish every real-time notification to that transport. Declared
                // against the IOrderNotification MARKER, not the concrete record,
                // so a new notification type is routed automatically instead of
                // silently going unroutable.
                opts.Publish(x => { x.MessagesImplementing<IOrderNotification>().ToSignalR(); });

                // PROD PERSISTENCE FIX (verified 2026-09-30): without AutoApplyTransactions,
                // Wolverine handlers that rely on the documented convention ("Wolverine handles
                // SaveChangesAsync automatically via AutoApplyTransactions" — see
                // SagaOrderCompletionHandlers) return success without flushing their DbContext:
                // POST /api/v1/inventory returned 201 with zero rows in inventory.stocks.
                // IntegrationTestFixture already sets this; production must mirror it.
                opts.Policies.AutoApplyTransactions();
            },
            typeof(CreateProductCommand),
            typeof(ReserveStockCommand),
            typeof(CreateOrderCommand),
            typeof(RefundPaymentTransactionCommand),
            typeof(CheckDailyReconciliation),

            // CRITICAL: Wolverine handler discovery is EXPLICIT — every assembly that
            // hosts message handlers MUST be listed, or its handlers silently do not
            // exist in Static (production) mode, and every message they handle fails
            // with IndeterminateRoutesException. Development/Test runs use Auto
            // discovery against this same assembly set. Keep this list in sync with
            // the handler assemblies (see IntegrationTestFixture for the test mirror).
            // Infrastructure assemblies (hosts all command/query handlers):
            typeof(NetCommerce.Catalog.Infrastructure.Handlers.CreateProductHandler),
            typeof(NetCommerce.Ordering.Infrastructure.Handlers.CreateOrderHandler),
            typeof(NetCommerce.Inventory.Infrastructure.Handlers.CreateStockHandler),
            typeof(NetCommerce.Payments.Infrastructure.Handlers.RefundPaymentTransactionHandler),
            typeof(NetCommerce.Finance.Infrastructure.Handlers.ReconciliationSchedulerHandler),
            // Shipping Application (hosts OrderReadyForShippingHandler):
            typeof(NetCommerce.Shipping.Application.Handlers.OrderReadyForShippingHandler));

        return host;
    }
}
