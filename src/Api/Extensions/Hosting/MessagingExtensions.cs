using JasperFx.CodeGeneration;
using NetCommerce.Catalog.Application.Products.Commands;
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
