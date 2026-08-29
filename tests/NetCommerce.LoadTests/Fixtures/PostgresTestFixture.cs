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
        if (_postgresContainer is not null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    public InventoryDbContext CreateInventoryDbContext()
    {
        var serviceProvider = CreateServiceProvider();
        var scope = serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    }

    public ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => Substitute.For<ITenantContext>());
        services.AddScoped<IUserContext>(_ => Substitute.For<IUserContext>());
        services.AddScoped<IMessageBus>(_ => Substitute.For<IMessageBus>());
        services.AddKernelEfCore<InventoryDbContext>(options => options.UseNpgsql(ConnectionString));

        return services.BuildServiceProvider();
    }

    public async Task ResetAsync()
    {
        await using var context = CreateInventoryDbContext();
        await context.Database.ExecuteSqlRawAsync("""
                                                  TRUNCATE TABLE inventory.stock_reservations, inventory.stocks CASCADE;
                                                  """);
    }
}

public sealed class ScopedDbContext<TContext> : IAsyncDisposable, IDisposable where TContext : DbContext
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

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        if (_scope is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            _scope.Dispose();
    }
}

[CollectionDefinition(nameof(PostgresTestCollection))]
public class PostgresTestCollection : ICollectionFixture<PostgresTestFixture>
{
}
