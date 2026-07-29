#nullable enable
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Infrastructure.Handlers;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Catalog.Infrastructure.Services;
using NetCommerce.Domain.Shared;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Infrastructure.Handlers;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Kernel.EfCore;
using NetCommerce.Kernel.Wolverine;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Handlers;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Payments.Infrastructure.Handlers;
using NetCommerce.Payments.Infrastructure.Persistence;
using Npgsql;
using NSubstitute;
using Respawn;
using Respawn.Graph;
using System;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RDBMS;
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
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            // Include all schemas to ensure true database cleanup and isolation
            SchemasToInclude = ["catalog", "inventory", "ordering", "payments", "finance", "wolverine", "public"],
            // Never truncate migration history
            TablesToIgnore = ["__EFMigrationsHistory"]
        });
    }

    private static bool IsDockerUnavailable(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("Docker", StringComparison.OrdinalIgnoreCase)
               || message.Contains("npipe://", StringComparison.OrdinalIgnoreCase)
               || message.Contains("No such host", StringComparison.OrdinalIgnoreCase)
               || message.Contains("connection", StringComparison.OrdinalIgnoreCase)
               || message.Contains("refused", StringComparison.OrdinalIgnoreCase);
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
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });

                logging.SetMinimumLevel(LogLevel.Warning);
                logging.AddFilter("NetCommerce", LogLevel.Error);
                logging.AddFilter("Wolverine", LogLevel.Warning);
                logging.AddFilter("JasperFx", LogLevel.Warning);
                logging.AddFilter("Microsoft", LogLevel.Warning);
                logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Error);
                logging.AddFilter("Npgsql", LogLevel.Warning);
            })
            .ConfigureServices((hostContext, services) =>
            {
                var mockTenantContext = Substitute.For<ITenantContext>();
                mockTenantContext.TenantId.Returns("test-tenant");
                mockTenantContext.HasTenant.Returns(true);
                services.AddSingleton(mockTenantContext);

                var mockUserContext = Substitute.For<IUserContext>();
                mockUserContext.UserId.Returns("test-user");
                services.AddSingleton(mockUserContext);
            })
            .UseWolverine(opts =>
            {
                // CRITICAL FOR WOLVERINE 6.0: Allow service location for factory-registered dependencies
                opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

                // Enable Roslyn runtime compilation for integration tests (Fixes missing IAssemblyGenerator exception)
                opts.UseRuntimeCompilation();
                opts.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Auto;

                // Register Saga for PostgreSQL storage (Fixes InvalidSagaException)
                opts.AddSagaType<NetCommerce.Ordering.Application.Sagas.OrderFulfillmentSaga>();

                // Configure Wolverine for testing - include Application assemblies for commands/queries
                opts.Discovery.IncludeAssembly(typeof(CreateProductCommand).Assembly);
                opts.Discovery.IncludeAssembly(typeof(CreateOrderCommand).Assembly);

                // Include Saga and handler assemblies (Application)
                opts.Discovery.IncludeAssembly(typeof(NetCommerce.Ordering.Application.Sagas.OrderFulfillmentSaga).Assembly);
                opts.Discovery.IncludeAssembly(typeof(NetCommerce.Inventory.Application.Stock.Commands.ReserveStockCommand).Assembly);
                opts.Discovery.IncludeAssembly(typeof(NetCommerce.Payments.Application.Transactions.Commands.RefundPaymentTransactionCommand).Assembly);

                // Include Infrastructure assemblies where Wolverine handlers live
                opts.Discovery.IncludeAssembly(typeof(CreateProductHandler).Assembly); // Catalog.Infrastructure
                opts.Discovery.IncludeAssembly(typeof(CreateOrderHandler).Assembly); // Ordering.Infrastructure
                opts.Discovery.IncludeAssembly(typeof(CreateStockHandler).Assembly); // Inventory.Infrastructure
                opts.Discovery.IncludeAssembly(typeof(RefundPaymentTransactionHandler).Assembly); // Payments.Infrastructure
                opts.Discovery.IncludeAssembly(typeof(NetCommerce.Finance.Infrastructure.Handlers.ReconciliationSchedulerHandler).Assembly); // Finance.Infrastructure

                // Use PostgreSQL persistence with outbox
                opts.PersistMessagesWithPostgresql(PostgresConnectionString, "wolverine");

                // Enable EF Core integration for transactional outbox
                opts.UseEntityFrameworkCoreTransactions();

                // Auto-apply transactions for handlers
                opts.Policies.AutoApplyTransactions();

                // Use durable local queue for reliability
                opts.LocalQueue("local")
                    .UseDurableInbox();
            })
            .ConfigureServices(services =>
            {
                // Register HttpClientFactory for handlers requiring IHttpClientFactory (e.g. CriticalFinancialAlertHandler)
                services.AddHttpClient();

                // Register Finance Alerting Options
                services.Configure<AlertingOptions>(options =>
                {
                    options.DiscrepancyAlertThreshold = 100.00m;
                    options.SendEmailAlerts = false;
                    options.FinanceAlertEmail = "finance-alerts@test.com";
                });

                // Register DbContexts using KERNEL EXTENSIONS (Wires up Interceptors)
                services.AddKernelEfCore<CatalogDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));
                services.AddKernelEfCore<InventoryDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));
                services.AddKernelEfCore<OrderingDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));
                services.AddKernelEfCore<PaymentsDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));
                services.AddKernelEfCore<NetCommerce.Finance.Infrastructure.Persistence.FinanceDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));

                // Domain Services & Repositories
                services.AddScoped<IPriceLookupService, OrderingPriceLookup>();
                services.AddScoped<NetCommerce.Finance.Domain.Reconciliation.IReconciliationSessionRepository, NetCommerce.Finance.Infrastructure.Persistence.Repositories.ReconciliationSessionRepository>();
                services.AddScoped<NetCommerce.Finance.Domain.Audit.IFinancialAuditRepository, NetCommerce.Finance.Infrastructure.Persistence.Repositories.FinancialAuditRepository>();
                services.AddScoped<NetCommerce.Finance.Domain.Webhooks.IWebhookEventStore, NetCommerce.Finance.Infrastructure.Persistence.Repositories.WebhookEventStore>();
                services.AddScoped<NetCommerce.Payments.Domain.Transactions.IPaymentTransactionRepository, NetCommerce.Payments.Infrastructure.Persistence.Repositories.PaymentTransactionRepository>();
                services.AddScoped<IPaymentTransactionReadService, NetCommerce.Payments.Infrastructure.Services.PaymentTransactionReadService>();
                services.AddScoped<NetCommerce.Ordering.Domain.Orders.IOrderRepository, NetCommerce.Ordering.Infrastructure.Persistence.Repositories.OrderRepository>();
                services.AddScoped<NetCommerce.Kernel.Application.IUnitOfWork>(sp => sp.GetRequiredService<NetCommerce.Finance.Infrastructure.Persistence.FinanceDbContext>());
                services.AddScoped<ReconciliationEngine>();
                services.AddSingleton<IPaymentGateway, TestPaymentGateway>();

                services.AddScoped<NetCommerce.Ordering.Application.Orders.Services.IPromotionEngine, NetCommerce.Ordering.Infrastructure.Services.SimplePromotionEngine>();
                services.AddScoped<ITaxProvider, NetCommerce.Ordering.Infrastructure.Services.LocalTaxProvider>();

                // Mocks
                services.AddScoped(_ => Substitute.For<Amazon.S3.IAmazonS3>());
                services.AddScoped(_ => Substitute.For<NetCommerce.Kernel.Application.Notifications.IEmailProvider>());
                services.AddScoped(_ => Substitute.For<NetCommerce.Kernel.Application.Notifications.ITemplateEngine>());
            });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    // Helper to create scopes for initialization
    public ScopedDbContext<T> CreateScopedDbContext<T>() where T : DbContext
    {
        var scope = Host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<T>();
        return new ScopedDbContext<T>(context, scope);
    }

    public CatalogDbContext CreateCatalogDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    }

    public InventoryDbContext CreateInventoryDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    }

    public OrderingDbContext CreateOrderingDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    }

    public PaymentsDbContext CreatePaymentsDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    }

    public NetCommerce.Finance.Infrastructure.Persistence.FinanceDbContext CreateFinanceDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<NetCommerce.Finance.Infrastructure.Persistence.FinanceDbContext>();
    }

    private async Task InitializeDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        // Create Schemas
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE SCHEMA IF NOT EXISTS catalog;
            CREATE SCHEMA IF NOT EXISTS inventory;
            CREATE SCHEMA IF NOT EXISTS ordering;
            CREATE SCHEMA IF NOT EXISTS payments;
            CREATE SCHEMA IF NOT EXISTS finance;
            CREATE SCHEMA IF NOT EXISTS wolverine;";
        await cmd.ExecuteNonQueryAsync();

        // Create Tables
        await CreateTablesFor<CatalogDbContext>();
        await CreateTablesFor<InventoryDbContext>();
        await CreateTablesFor<OrderingDbContext>();
        await CreateTablesFor<PaymentsDbContext>();
        await CreateTablesFor<NetCommerce.Finance.Infrastructure.Persistence.FinanceDbContext>();
    }

    private async Task CreateTablesFor<T>() where T : DbContext
    {
        using var scoped = CreateScopedDbContext<T>();
        await scoped.Context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
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

public class ScopedDbContext<TContext> : IDisposable where TContext : DbContext
{
    public TContext Context { get; }
    private readonly IServiceScope _scope;

    public ScopedDbContext(TContext context, IServiceScope scope)
    {
        Context = context;
        _scope = scope;
    }

    public void Dispose()
    {
        Context.Dispose();
        _scope.Dispose();
    }
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
    private IServiceScope _scope = null!;

    protected IntegrationTestBase(IntegrationTestFixture fixture)
    {
        Fixture = fixture;
    }

    protected IServiceProvider Services => _scope.ServiceProvider;

    public virtual async Task InitializeAsync()
    {
        await Fixture.ResetDatabaseAsync();
        TestPaymentGateway.Reset();

        _scope = Fixture.Host.Services.CreateScope();
    }

    public virtual async Task DisposeAsync()
    {
        if (_scope is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            _scope.Dispose();
    }

    protected T GetService<T>() where T : notnull
        => Services.GetRequiredService<T>();
}

internal sealed class TestPaymentGateway : IPaymentGateway
{
    public static HashSet<Guid> FailingOrderIds { get; } = [];
    public static HashSet<decimal> FailingAmounts { get; } = [];

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
        if (FailingOrderIds.Contains(request.OrderId))
        {
            return Task.FromResult(Result.Failure<PaymentResult>(
                Error.Failure("Payment.Failed", "Simulated payment failure for testing")));
        }

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

    public Task<Result<PaymentResult>> GetPaymentStatusAsync(
        string externalTransactionId,
        CancellationToken cancellationToken = default)
    {
        var result = new PaymentResult(
            TransactionId: externalTransactionId,
            Status: PaymentResultStatus.Succeeded);

        return Task.FromResult(Result.Success(result));
    }
}
