using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NSubstitute;
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence;
using Npgsql;

namespace NetCommerce.Integration.Tests.Fixtures;

/// <summary>
/// Shared fixture for integration tests using Testcontainers.
/// Provides PostgreSQL and Redis containers with Respawn for database cleanup.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;
    private RedisContainer _redisContainer = null!;
    private Respawner _respawner = null!;
    
    public string PostgresConnectionString => _postgresContainer.GetConnectionString();
    public string RedisConnectionString => _redisContainer.GetConnectionString();
    
    // DbContext factories
    public CatalogDbContext CreateCatalogDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;
        
        return new CatalogDbContext(options, Substitute.For<IMediator>());
    }
    
    public InventoryDbContext CreateInventoryDbContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;
        
        return new InventoryDbContext(options, Substitute.For<IMediator>());
    }
    
    public OrderingDbContext CreateOrderingDbContext()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;
        
        return new OrderingDbContext(options, Substitute.For<IMediator>());
    }

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("netcommerce_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        // Start Redis container
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _redisContainer.StartAsync());

        // Create schemas and migrate
        await InitializeDatabaseAsync();

        // Initialize Respawner for database cleanup
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["catalog", "inventory", "ordering", "public"],
            TablesToIgnore = ["__EFMigrationsHistory"]
        });
    }

    private async Task InitializeDatabaseAsync()
    {
        // Create database and schemas
        // Note: EnsureCreatedAsync() only works for the first context because after
        // the database exists, subsequent calls skip schema creation.
        // We need to use GetService<IRelationalDatabaseCreator>().CreateTables() instead.
        
        // Initialize Catalog schema
        await using var catalogContext = CreateCatalogDbContext();
        await catalogContext.Database.EnsureCreatedAsync();
        
        // Initialize Inventory schema
        // Database already exists, so we need to create tables directly
        await using var inventoryContext = CreateInventoryDbContext();
        var inventoryCreator = inventoryContext.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        await inventoryCreator.CreateTablesAsync();
        
        // Initialize Ordering schema
        await using var orderingContext = CreateOrderingDbContext();
        var orderingCreator = orderingContext.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        await orderingCreator.CreateTablesAsync();
    }

    /// <summary>
    /// Resets the database to a clean state using Respawn.
    /// Call this before each test.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}

/// <summary>
/// Collection definition for sharing IntegrationTestFixture across tests.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}

/// <summary>
/// Base class for integration tests with automatic database reset.
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
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
