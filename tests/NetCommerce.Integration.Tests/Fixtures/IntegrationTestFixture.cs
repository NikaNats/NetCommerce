#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Infrastructure.Handlers;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Infrastructure.Handlers;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Infrastructure.Handlers;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Payments.Infrastructure.Handlers;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Infrastructure.Messaging;
using NetCommerce.SharedKernel.Results;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Fixtures;

/// <summary>
///     Shared fixture for integration tests using Testcontainers.
///     Provides PostgreSQL and Redis containers with Respawn for database cleanup.
///     Configured with Wolverine for message tracking and transactional outbox testing.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;
    private RedisContainer _redisContainer = null!;
    private Respawner _respawner = null!;
    private IHost? _host;

    public string PostgresConnectionString => _postgresContainer.GetConnectionString();
    public string RedisConnectionString => _redisContainer.GetConnectionString();

    /// <summary>
    ///     Gets the configured host with Wolverine for tracked session testing.
    /// </summary>
    public IHost Host => _host ?? throw new InvalidOperationException("Host not initialized");

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("netcommerce_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        // Start Redis container
        _redisContainer = new RedisBuilder("redis:8-alpine")
            .Build();

        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _redisContainer.StartAsync());

        // Build host with Wolverine configured
        _host = await BuildTestHostAsync();

        // Create schemas and migrate
        await InitializeDatabaseAsync();

        // Initialize Respawner for database cleanup
        // Explicitly include wolverine schema to ensure outbox envelopes are cleared between tests
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            // Include wolverine schema for outbox tables (wolverine_incoming_envelopes, wolverine_outgoing_envelopes)
            // This prevents test contamination from messages persisted during previous test runs
            SchemasToInclude = ["catalog", "inventory", "ordering", "payments", "wolverine", "public"],
            // Never truncate migration history
            TablesToIgnore = ["__EFMigrationsHistory"],
            // Explicitly include wolverine envelope tables to prevent outbox leakage
            TablesToInclude =
            [
                new Table("wolverine", "wolverine_incoming_envelopes"),
                new Table("wolverine", "wolverine_outgoing_envelopes"),
                new Table("wolverine", "wolverine_dead_letters")
            ]
        });
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    /// <summary>
    ///     Builds a test host with Wolverine configured for tracking sessions.
    /// </summary>
    private async Task<IHost> BuildTestHostAsync()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                // Configure Wolverine for testing - include Application assemblies for commands/queries
                opts.Discovery.IncludeAssembly(typeof(CreateProductCommand).Assembly);
                opts.Discovery.IncludeAssembly(typeof(CreateOrderCommand).Assembly);

                // Include Saga and handler assemblies (Application)
                opts.Discovery.IncludeAssembly(typeof(NetCommerce.Ordering.Application.Sagas.OrderFulfillmentSaga).Assembly);
                opts.Discovery.IncludeAssembly(typeof(NetCommerce.Inventory.Application.Stock.Commands.ReserveStockCommand).Assembly);
                opts.Discovery.IncludeAssembly(typeof(NetCommerce.Payments.Application.Transactions.Commands.RefundPaymentTransactionCommand).Assembly);

                // CRITICAL: Include Infrastructure assemblies where Wolverine handlers live
                opts.Discovery.IncludeAssembly(typeof(CreateProductHandler).Assembly); // Catalog.Infrastructure
                opts.Discovery.IncludeAssembly(typeof(CreateOrderHandler).Assembly); // Ordering.Infrastructure
                opts.Discovery.IncludeAssembly(typeof(CreateStockHandler).Assembly); // Inventory.Infrastructure
                opts.Discovery.IncludeAssembly(typeof(RefundPaymentTransactionHandler).Assembly); // Payments.Infrastructure

                // Use PostgreSQL persistence with outbox
                opts.PersistMessagesWithPostgresql(PostgresConnectionString, "wolverine");

                // CRITICAL: Enable EF Core integration for transactional outbox
                opts.UseEntityFrameworkCoreTransactions();

                // Auto-apply transactions for handlers
                opts.Policies.AutoApplyTransactions();

                // Use durable local queue for reliability
                opts.LocalQueue("local")
                    .UseDurableInbox();
            })
            .ConfigureServices(services =>
            {
                // Register DbContexts
                services.AddDbContext<CatalogDbContext>(options =>
                    options.UseNpgsql(PostgresConnectionString));
                services.AddDbContext<InventoryDbContext>(options =>
                    options.UseNpgsql(PostgresConnectionString));
                services.AddDbContext<OrderingDbContext>(options =>
                    options.UseNpgsql(PostgresConnectionString));
                services.AddDbContext<PaymentsDbContext>(options =>
                    options.UseNpgsql(PostgresConnectionString));

                // Register mock payment gateway for testing
                services.AddSingleton<IPaymentGateway, TestPaymentGateway>();
            });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    // DbContext factories
    public CatalogDbContext CreateCatalogDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        return new CatalogDbContext(options);
    }

    public InventoryDbContext CreateInventoryDbContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        return new InventoryDbContext(options);
    }

    public OrderingDbContext CreateOrderingDbContext()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        return new OrderingDbContext(options);
    }

    public PaymentsDbContext CreatePaymentsDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        return new PaymentsDbContext(options);
    }

    private async Task InitializeDatabaseAsync()
    {
        // Create schemas first using raw SQL
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE SCHEMA IF NOT EXISTS catalog;
            CREATE SCHEMA IF NOT EXISTS inventory;
            CREATE SCHEMA IF NOT EXISTS ordering;
            CREATE SCHEMA IF NOT EXISTS payments;
            CREATE SCHEMA IF NOT EXISTS wolverine;
        ";
        await cmd.ExecuteNonQueryAsync();

        // Initialize Catalog schema
        await using var catalogContext = CreateCatalogDbContext();
        var catalogCreator = catalogContext.GetService<IRelationalDatabaseCreator>();
        await catalogCreator.CreateTablesAsync();

        // Initialize Inventory schema
        await using var inventoryContext = CreateInventoryDbContext();
        var inventoryCreator = inventoryContext.GetService<IRelationalDatabaseCreator>();
        await inventoryCreator.CreateTablesAsync();

        // Initialize Ordering schema
        await using var orderingContext = CreateOrderingDbContext();
        var orderingCreator = orderingContext.GetService<IRelationalDatabaseCreator>();
        await orderingCreator.CreateTablesAsync();

        // Initialize Payments schema
        await using var paymentsContext = CreatePaymentsDbContext();
        var paymentsCreator = paymentsContext.GetService<IRelationalDatabaseCreator>();
        await paymentsCreator.CreateTablesAsync();
    }

    /// <summary>
    ///     Resets the database to a clean state using Respawn.
    ///     Call this before each test.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    /// <summary>
    ///     Starts a Wolverine tracked session for testing message flows.
    /// </summary>
    public TrackedSessionConfiguration StartTrackedSession() => Host.TrackActivity();
}

/// <summary>
///     Collection definition for sharing IntegrationTestFixture across tests.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}

/// <summary>
///     Base class for integration tests with automatic database reset.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly IntegrationTestFixture Fixture;

    protected IntegrationTestBase(IntegrationTestFixture fixture)
    {
        Fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await Fixture.ResetDatabaseAsync();
        // Reset test payment gateway configuration for each test
        TestPaymentGateway.Reset();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Configurable test payment gateway for integration tests.
///     Supports simulating failures for specific order IDs or amounts.
/// </summary>
internal sealed class TestPaymentGateway : IPaymentGateway
{
    /// <summary>
    ///     Order IDs that should fail payment processing.
    /// </summary>
    public static HashSet<Guid> FailingOrderIds { get; } = [];

    /// <summary>
    ///     Payment amounts (decimal values) that should fail (useful for testing specific scenarios).
    ///     Example: 666.00m could trigger a failure.
    /// </summary>
    public static HashSet<decimal> FailingAmounts { get; } = [];

    /// <summary>
    ///     Resets the failure configuration (call in test cleanup).
    /// </summary>
    public static void Reset()
    {
        FailingOrderIds.Clear();
        FailingAmounts.Clear();
    }

    public PaymentProvider Provider => PaymentProvider.Stripe;

    public Task<Result<PaymentResult>> ProcessPaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        // Check if this order should fail
        if (FailingOrderIds.Contains(request.OrderId))
        {
            return Task.FromResult(Result.Failure<PaymentResult>(
                Error.Failure("Payment.Failed", "Simulated payment failure for testing")));
        }

        // Check if this amount should fail (compare the decimal value from Money)
        if (FailingAmounts.Contains(request.Amount.Amount))
        {
            return Task.FromResult(Result.Failure<PaymentResult>(
                Error.Failure("Payment.DeclinedAmount", $"Payment declined for amount {request.Amount}")));
        }

        var result = new PaymentResult(
            TransactionId: $"test_txn_{Guid.NewGuid():N}",
            Status: PaymentResultStatus.Succeeded);

        return Task.FromResult(Result.Success(result));
    }

    public Task<Result<RefundResult>> ProcessRefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new RefundResult(
            RefundId: $"test_refund_{Guid.NewGuid():N}",
            Success: true);

        return Task.FromResult(Result.Success(result));
    }
}
