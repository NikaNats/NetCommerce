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
    private ServiceProvider _serviceProvider = null!;

    public string ConnectionString => _postgresContainer.GetConnectionString();

    // xUnit v3: ValueTask InitializeAsync
    public async ValueTask InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("loadtest_db")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgresContainer.StartAsync();

        _serviceProvider = CreateServiceProvider();

        await using var scoped = CreateScopedDbContext();
        await scoped.Context.Database.EnsureCreatedAsync();
    }

    // xUnit v3: ValueTask DisposeAsync
    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    public ScopedDbContext<InventoryDbContext> CreateScopedDbContext()
    {
        var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return new ScopedDbContext<InventoryDbContext>(context, scope);
    }

    public InventoryDbContext CreateInventoryDbContext()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    }

    private ServiceProvider CreateServiceProvider()
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
        await using var scoped = CreateScopedDbContext();
        await scoped.Context.Database.ExecuteSqlRawAsync("""
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
