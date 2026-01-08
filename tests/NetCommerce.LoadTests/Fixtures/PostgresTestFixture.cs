#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.EfCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Wolverine;

namespace NetCommerce.LoadTests.Fixtures;

/// <summary>
///     Test fixture providing a real PostgreSQL container for load/concurrency tests.
///     Using in-memory database for concurrency tests produces false positives since
///     it doesn't properly simulate row-level locking and transaction isolation.
/// </summary>
public sealed class PostgresTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;

    public string ConnectionString => _postgresContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("loadtest_db")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgresContainer.StartAsync();

        // Create schema and tables
        await using var context = CreateInventoryDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    /// <summary>
    ///     Creates a new InventoryDbContext connected to the test PostgreSQL container.
    /// </summary>
    public InventoryDbContext CreateInventoryDbContext()
    {
        // WARNING: This creates a scope but doesn't dispose it, causing a resource leak.
        // For load tests (short-lived), this is acceptable to avoid breaking existing code.
        // In production code, use ScopedDbContext pattern as in IntegrationTestFixture.

        var serviceProvider = CreateServiceProvider();
        var scope = serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    }

    /// <summary>
    ///     Creates a ServiceProvider with InventoryDbContext configured for PostgreSQL.
    /// </summary>
    public ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Add kernel contexts
        services.AddScoped<ITenantContext>(_ => Substitute.For<ITenantContext>());
        services.AddScoped<IUserContext>(_ => Substitute.For<IUserContext>());

        // Add mock message bus for domain event interceptor
        services.AddScoped<IMessageBus>(_ => Substitute.For<IMessageBus>());

        // Add kernel EF Core with interceptors
        services.AddKernelEfCore<InventoryDbContext>(options => options.UseNpgsql(ConnectionString));

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Resets the database by truncating all tables.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var context = CreateInventoryDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE inventory.stock_reservations, inventory.stocks CASCADE;
            """);
    }
}

/// <summary>
///     Collection definition for sharing PostgresTestFixture across tests.
/// </summary>
[CollectionDefinition(nameof(PostgresTestCollection))]
public class PostgresTestCollection : ICollectionFixture<PostgresTestFixture>
{
}
