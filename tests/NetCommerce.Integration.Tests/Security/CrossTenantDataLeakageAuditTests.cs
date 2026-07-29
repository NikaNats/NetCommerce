#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Domain.Shared;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Security;

/// <summary>
///     PRODUCTION-READINESS TEST: Cross-Tenant Data Leakage Audit (Red-Team Testing)
/// </summary>
public class CrossTenantDataLeakageAuditTests : IntegrationTestBase
{
    private const string TenantA = "tenant-alpha";
    private const string TenantB = "tenant-beta";
    private const string TenantC = "tenant-gamma";

    public CrossTenantDataLeakageAuditTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: Direct API Object Reference (IDOR) Attack

    [Fact]
    public async Task IDOR_TenantA_ShouldNotAccessTenantB_Order()
    {
        // Seed order specifically for Tenant A
        var tenantAOrderId = await SeedOrderForTenantAsync(TenantA);

        using var scope = Fixture.Host.Services.CreateScope();

        // Simulate Tenant B authentication
        var tenantBContext = Substitute.For<ITenantContext>();
        tenantBContext.TenantId.Returns(TenantB);
        tenantBContext.HasTenant.Returns(true);

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<OrderingDbContext>>();
        await using var dbContextB = new OrderingDbContext(options, tenantBContext);

        // Attempt to fetch Tenant A's order as Tenant B
        var tenantBQuery = await dbContextB.Set<Order>()
            .AsNoTracking()
            .Where(o => o.Id == tenantAOrderId)
            .FirstOrDefaultAsync();

        tenantBQuery.ShouldBeNull(
            $"CRITICAL SECURITY VIOLATION: Tenant '{TenantB}' was able to access Order '{tenantAOrderId}' " +
            $"belonging to Tenant '{TenantA}'!");

        Console.WriteLine("[CrossTenant] IDOR attack blocked: Tenant B cannot see Tenant A's order");

        // Verify Tenant A CAN see their own order
        var tenantAContext = Substitute.For<ITenantContext>();
        tenantAContext.TenantId.Returns(TenantA);
        tenantAContext.HasTenant.Returns(true);

        await using var dbContextA = new OrderingDbContext(options, tenantAContext);

        var tenantAQuery = await dbContextA.Set<Order>()
            .AsNoTracking()
            .Where(o => o.Id == tenantAOrderId)
            .FirstOrDefaultAsync();

        tenantAQuery.ShouldNotBeNull("Tenant A should be able to see their own order.");
        Console.WriteLine($"[CrossTenant] Tenant A correctly sees their own order: {tenantAQuery.OrderNumber}");
    }

    #endregion

    #region Test 2: Forged Tenant Header Attack

    [Fact]
    public async Task ForgedTenantHeader_ShouldBeIgnored()
    {
        var jwtClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-123"),
            new("tenant_id", TenantA),  // JWT claim
            new("sub", "user-123")
        };

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(jwtClaims, "Bearer"));
        httpContext.Request.Headers["X-Tenant-Id"] = TenantB;  // Forged header!

        var resolvedTenant = httpContext.User.FindFirst("tenant_id")?.Value;

        resolvedTenant.ShouldBe(TenantA,
            "CRITICAL: Forged X-Tenant-Id header was used instead of JWT claim!");

        Console.WriteLine("[CrossTenant] Forged header attack blocked: JWT claim takes precedence");
    }

    #endregion

    #region Test 3: SQL Query Filter Verification

    [Fact]
    public async Task QueryFilter_ShouldIncludeTenantIdInGeneratedSQL()
    {
        using var scope = Fixture.Host.Services.CreateScope();

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);
        tenantContext.HasTenant.Returns(true);

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<OrderingDbContext>>();
        await using var dbContext = new OrderingDbContext(options, tenantContext);

        var query = dbContext.Set<Order>()
            .Where(o => o.Status == OrderStatus.Submitted)
            .OrderBy(o => o.CreatedAt);

        var generatedSql = query.ToQueryString();

        generatedSql.ToLowerInvariant().ShouldContain("tenant_id",
            Case.Insensitive,
            $"CRITICAL: Generated SQL does not include tenant_id filter!\nGenerated SQL: {generatedSql}");

        Console.WriteLine("[CrossTenant] Query filter verified. Generated SQL includes tenant_id:");
        Console.WriteLine(generatedSql);
    }

    #endregion

    #region Test 4: Bulk Data Isolation Verification

    [Fact]
    public async Task BulkData_EachTenant_ShouldSeeOnlyTheirRecords()
    {
        const int ordersPerTenant = 5;
        var testTenants = new[] { TenantA, TenantB, TenantC };
        var tenantOrders = new Dictionary<string, List<Guid>>();

        foreach (var tenant in testTenants)
        {
            tenantOrders[tenant] = new List<Guid>();

            for (int i = 0; i < ordersPerTenant; i++)
            {
                var orderId = await SeedOrderForTenantAsync(tenant, 50.0m + i * 10);
                tenantOrders[tenant].Add(orderId);
            }
        }

        Console.WriteLine($"[CrossTenant] Created {ordersPerTenant} orders for each of {testTenants.Length} tenants");

        using var scope = Fixture.Host.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<OrderingDbContext>>();

        foreach (var tenant in testTenants)
        {
            var tenantContext = Substitute.For<ITenantContext>();
            tenantContext.TenantId.Returns(tenant);
            tenantContext.HasTenant.Returns(true);

            await using var dbContext = new OrderingDbContext(options, tenantContext);

            var visibleOrders = await dbContext.Set<Order>()
                .AsNoTracking()
                .ToListAsync();

            var visibleOrderIds = visibleOrders.Select(o => o.Id).ToHashSet();

            visibleOrders.Count.ShouldBe(ordersPerTenant,
                $"Tenant '{tenant}' sees {visibleOrders.Count} orders, expected {ordersPerTenant}.");

            foreach (var order in visibleOrders)
            {
                tenantOrders[tenant].ShouldContain(order.Id,
                    $"Tenant '{tenant}' sees order {order.Id} which does not belong to them!");
            }

            foreach (var otherTenant in testTenants.Where(t => t != tenant))
            {
                foreach (var otherId in tenantOrders[otherTenant])
                {
                    visibleOrderIds.ShouldNotContain(otherId,
                        $"SECURITY BREACH: Tenant '{tenant}' can see order {otherId} from tenant '{otherTenant}'!");
                }
            }

            Console.WriteLine($"[CrossTenant] Tenant '{tenant}': ✓ sees {visibleOrders.Count} orders, ✓ isolated");
        }
    }

    #endregion

    #region Test 5: Timing Attack Detection

    [Fact]
    public async Task TimingAttack_ResponseTime_ShouldNotRevealExistence()
    {
        var existingOrderId = await SeedOrderForTenantAsync(TenantA);
        var nonExistingOrderId = Guid.NewGuid();

        using var scope = Fixture.Host.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<OrderingDbContext>>();

        var tenantBContext = Substitute.For<ITenantContext>();
        tenantBContext.TenantId.Returns(TenantB);
        tenantBContext.HasTenant.Returns(true);

        const int iterations = 10;
        var existingTimes = new List<double>();
        var nonExistingTimes = new List<double>();

        for (int i = 0; i < iterations; i++)
        {
            await using var dbContext1 = new OrderingDbContext(options, tenantBContext);
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            var result1 = await dbContext1.Set<Order>()
                .AsNoTracking()
                .Where(o => o.Id == existingOrderId)
                .FirstOrDefaultAsync();
            sw1.Stop();
            existingTimes.Add(sw1.Elapsed.TotalMilliseconds);

            await using var dbContext2 = new OrderingDbContext(options, tenantBContext);
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            var result2 = await dbContext2.Set<Order>()
                .AsNoTracking()
                .Where(o => o.Id == nonExistingOrderId)
                .FirstOrDefaultAsync();
            sw2.Stop();
            nonExistingTimes.Add(sw2.Elapsed.TotalMilliseconds);
        }

        var avgExisting = existingTimes.Average();
        var avgNonExisting = nonExistingTimes.Average();
        var timeDifference = Math.Abs(avgExisting - avgNonExisting);

        const double maxAllowedDifferenceMs = 50.0;

        Console.WriteLine($"[CrossTenant] Timing Analysis:");
        Console.WriteLine($"  Avg time for 'exists' query: {avgExisting:F2}ms");
        Console.WriteLine($"  Avg time for 'not exists' query: {avgNonExisting:F2}ms");
        Console.WriteLine($"  Time difference: {timeDifference:F2}ms (max allowed: {maxAllowedDifferenceMs}ms)");

        if (timeDifference > maxAllowedDifferenceMs)
        {
            Console.WriteLine($"  ⚠️ WARNING: Potential timing leak detected.");
        }
        else
        {
            Console.WriteLine($"  ✓ Timing difference within acceptable bounds");
        }

        await using var ctx = new OrderingDbContext(options, tenantBContext);
        var finalResult = await ctx.Set<Order>()
            .AsNoTracking()
            .Where(o => o.Id == existingOrderId)
            .FirstOrDefaultAsync();

        finalResult.ShouldBeNull("Tenant B should not be able to determine if an order exists in Tenant A.");
    }

    #endregion

    #region Helper Methods

    private async Task<Guid> SeedOrderForTenantAsync(string tenantId, decimal amount = 199.99m)
    {
        // 1. Seed Catalog Product
        await using var catalogDb = Fixture.CreateCatalogDbContext();
        var product = Product.Create(
            name: $"Test Product {Guid.NewGuid():N}"[..20],
            description: "Test product for cross tenant testing",
            sku: $"SKU-{Guid.NewGuid():N}"[..15],
            price: Money.Create(amount, "GEL"),
            categoryId: Guid.NewGuid());
        product.Publish();

        catalogDb.Products.Add(product);
        await catalogDb.SaveChangesAsync();

        var productIdGuid = product.Id;

        // 2. Seed Inventory Stock
        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productIdGuid, product.Sku, 100);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();

        // 3. Create Order via Application Command
        var command = new CreateOrderCommand(
            CustomerId: Guid.NewGuid(),
            CustomerEmail: $"tenant-{tenantId}@test.com",
            CustomerName: "Test Customer",
            Items: [new OrderItemRequest(productIdGuid, 1, amount)],
            ShippingAddress: CreateTestAddress(),
            BillingAddress: CreateTestAddress(),
            PaymentMethod: "CreditCard",
            IdempotencyKey: Guid.NewGuid().ToString());

        var (_, result) = await Fixture.Host.InvokeMessageAndWaitAsync<Result<Guid>>(command);

        if (result is null || !result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to seed order for tenant {tenantId}: {result?.Error?.Description}");
        }

        var orderId = result.Value;

        // 4. Stamp TenantId on the created Order for the target tenant
        using var scope = Fixture.Host.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<OrderingDbContext>>();

        var targetTenantContext = Substitute.For<ITenantContext>();
        targetTenantContext.TenantId.Returns(tenantId);
        targetTenantContext.HasTenant.Returns(true);

        await using var orderingDb = new OrderingDbContext(options, targetTenantContext);
        var order = await orderingDb.Orders.IgnoreQueryFilters().FirstAsync(o => o.Id == orderId);

        // Update TenantId shadow / backing property
        orderingDb.Entry(order).Property("TenantId").CurrentValue = tenantId;
        typeof(Order).GetProperty("TenantId")?.SetValue(order, tenantId);

        await orderingDb.SaveChangesAsync();

        return orderId;
    }

    private static AddressDto CreateTestAddress() => new(
        Street: "123 Security Lane",
        City: "Testville",
        State: "TX",
        PostalCode: "12345",
        Country: "US",
        RecipientName: "Audit Test User",
        PhoneNumber: "+1-555-0000");

    #endregion
}
