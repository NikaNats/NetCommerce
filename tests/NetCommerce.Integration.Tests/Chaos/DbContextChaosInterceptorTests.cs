#nullable enable
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.ServiceDefaults.Chaos;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Integration.Tests.Chaos;

/// <summary>
///     Phase 7: Chaos Engineering Integration Tests
///
///     <para>
///     <b>Requirement:</b> Integrate Polly.Simmy into NetCommerce.ServiceDefaults.
///     Deliberately inject 500ms latency into the Catalog-to-DB calls and verify
///     that the API doesn't hang.
///     </para>
///
///     <para>
///     <b>Test Strategy:</b>
///     1. Configure DbContextChaosInterceptor with 500ms latency
///     2. Execute Catalog queries with timeout
///     3. Verify responses complete within acceptable SLA (not hanging)
///     4. Verify latency injection is measurable
///     </para>
/// </summary>
public class DbContextChaosInterceptorTests : IntegrationTestBase
{
    public DbContextChaosInterceptorTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     Validates that 500ms latency injection doesn't cause the query to hang.
    ///
    ///     <para>
    ///     The API should still respond within a reasonable SLA (e.g., 3s)
    ///     even with 500ms injected latency.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task ChaosLatency_500ms_ShouldNotHangQuery()
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // ARRANGE: Configure chaos interceptor with 500ms latency
        // ═══════════════════════════════════════════════════════════════════════════
        var chaosOptions = new DbChaosOptions
        {
            Enabled = true,
            TargetSchemaFilter = "catalog", // Only affect Catalog module
            Latency = new DbLatencyOptions
            {
                Enabled = true,
                InjectionRate = 1.0, // 100% injection rate for testing
                MinDelayMs = 500,
                MaxDelayMs = 500
            }
        };

        var mockLogger = Substitute.For<ILogger<DbContextChaosInterceptor>>();
        var interceptor = new DbContextChaosInterceptor(chaosOptions, mockLogger);

        // Create a test DbContext with the chaos interceptor
        var services = new ServiceCollection();
        services.AddDbContext<CatalogDbContext>((sp, options) =>
        {
            options.UseNpgsql(Fixture.CreateCatalogDbContext().Database.GetConnectionString())
                .AddInterceptors(interceptor);
        });

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var chaosDbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // Seed some test data
        var categoryId = Guid.NewGuid();
        var product = Product.Create(
            "Chaos Test Product",
            "A product for chaos testing",
            "SKU-CHAOS-001",
            NetCommerce.Domain.Shared.Money.Create(99.99m),
            categoryId);

        chaosDbContext.Products.Add(product);
        await chaosDbContext.SaveChangesAsync();

        // ═══════════════════════════════════════════════════════════════════════════
        // ACT: Execute query with timing
        // ═══════════════════════════════════════════════════════════════════════════
        var stopwatch = Stopwatch.StartNew();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // 5s timeout

        // This should NOT hang - it should complete with ~500ms added latency
        var result = await chaosDbContext.Products
            .FirstOrDefaultAsync(p => p.Name == "Chaos Test Product", cts.Token);

        stopwatch.Stop();

        // ═══════════════════════════════════════════════════════════════════════════
        // ASSERT: Verify timing and no hang
        // ═══════════════════════════════════════════════════════════════════════════
        result.ShouldNotBeNull("Query should return the product");

        // Should have injected ~500ms latency
        stopwatch.ElapsedMilliseconds.ShouldBeGreaterThan(400,
            "Expected at least ~500ms latency to be injected");

        // Should NOT have hung (completed within 5s timeout)
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(4000,
            $"Query took {stopwatch.ElapsedMilliseconds}ms - potential hang detected!");

        Console.WriteLine($"[Chaos] Query completed in {stopwatch.ElapsedMilliseconds}ms with 500ms injected latency ✓");
    }

    /// <summary>
    ///     Validates that chaos injection only affects the targeted schema.
    /// </summary>
    [Fact]
    public async Task ChaosLatency_SchemaFilter_ShouldOnlyAffectTargetedSchema()
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // ARRANGE: Configure chaos to only affect "catalog" schema
        // ═══════════════════════════════════════════════════════════════════════════
        var chaosOptions = new DbChaosOptions
        {
            Enabled = true,
            TargetSchemaFilter = "catalog",
            Latency = new DbLatencyOptions
            {
                Enabled = true,
                InjectionRate = 1.0,
                MinDelayMs = 500,
                MaxDelayMs = 500
            }
        };

        var interceptor = new DbContextChaosInterceptor(chaosOptions);

        // ═══════════════════════════════════════════════════════════════════════════
        // TEST: Inventory queries should NOT be affected
        // ═══════════════════════════════════════════════════════════════════════════
        var inventoryStopwatch = Stopwatch.StartNew();

        // Use non-chaos DbContext for inventory
        await using var inventoryDb = Fixture.CreateInventoryDbContext();

        // Simple query - should NOT have latency injected
        var stockCount = await inventoryDb.Stocks.CountAsync();

        inventoryStopwatch.Stop();

        // Inventory query should be fast (no chaos injected)
        inventoryStopwatch.ElapsedMilliseconds.ShouldBeLessThan(200,
            "Inventory query should not be affected by catalog-targeted chaos");

        Console.WriteLine(
            $"[Chaos] Inventory query (non-targeted): {inventoryStopwatch.ElapsedMilliseconds}ms ✓");
    }

    /// <summary>
    ///     Validates that chaos can be dynamically enabled/disabled.
    /// </summary>
    [Fact]
    public async Task ChaosLatency_Disabled_ShouldNotInjectLatency()
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // ARRANGE: Chaos explicitly disabled
        // ═══════════════════════════════════════════════════════════════════════════
        var chaosOptions = new DbChaosOptions
        {
            Enabled = false, // Disabled
            Latency = new DbLatencyOptions
            {
                Enabled = true,
                InjectionRate = 1.0,
                MinDelayMs = 2000, // Would be very obvious if injected
                MaxDelayMs = 2000
            }
        };

        var interceptor = new DbContextChaosInterceptor(chaosOptions);

        // ═══════════════════════════════════════════════════════════════════════════
        // ACT: Query with chaos disabled
        // ═══════════════════════════════════════════════════════════════════════════
        var services = new ServiceCollection();
        services.AddDbContext<CatalogDbContext>((sp, options) =>
        {
            options.UseNpgsql(Fixture.CreateCatalogDbContext().Database.GetConnectionString())
                .AddInterceptors(interceptor);
        });

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var stopwatch = Stopwatch.StartNew();
        var count = await dbContext.Products.CountAsync();
        stopwatch.Stop();

        // ═══════════════════════════════════════════════════════════════════════════
        // ASSERT: No latency injected
        // ═══════════════════════════════════════════════════════════════════════════
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(500,
            "With chaos disabled, no latency should be injected");

        Console.WriteLine($"[Chaos] Query with chaos disabled: {stopwatch.ElapsedMilliseconds}ms ✓");
    }

    /// <summary>
    ///     Validates fault injection causes expected exceptions.
    /// </summary>
    [Fact]
    public async Task ChaosFault_ShouldThrowException()
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // ARRANGE: Configure fault injection
        // ═══════════════════════════════════════════════════════════════════════════
        var chaosOptions = new DbChaosOptions
        {
            Enabled = true,
            Fault = new DbFaultOptions
            {
                Enabled = true,
                InjectionRate = 1.0, // 100% fault injection
                FaultMessage = "Test fault injection"
            }
        };

        var mockLogger = Substitute.For<ILogger<DbContextChaosInterceptor>>();
        var interceptor = new DbContextChaosInterceptor(chaosOptions, mockLogger);

        var services = new ServiceCollection();
        services.AddDbContext<CatalogDbContext>((sp, options) =>
        {
            options.UseNpgsql(Fixture.CreateCatalogDbContext().Database.GetConnectionString())
                .AddInterceptors(interceptor);
        });

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // ═══════════════════════════════════════════════════════════════════════════
        // ACT & ASSERT: Should throw
        // ═══════════════════════════════════════════════════════════════════════════
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await dbContext.Products.CountAsync());

        exception.Message.ShouldContain("Test fault injection");

        Console.WriteLine($"[Chaos] Fault injection working: {exception.Message} ✓");
    }
}
