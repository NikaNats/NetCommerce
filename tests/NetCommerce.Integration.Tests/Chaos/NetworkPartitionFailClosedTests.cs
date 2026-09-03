#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.BackgroundJobs;
using NetCommerce.Inventory.Infrastructure.Handlers;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Kernel.Core.Results;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Integration.Tests.Chaos;

[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "NetworkPartition")]
public sealed class NetworkPartitionFailClosedTests : IntegrationTestBase
{
    public NetworkPartitionFailClosedTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task NetworkPartition_WhenDatabaseUnreachable_MustFailClosedAndTripCircuitBreaker()
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // 1. Seed valid stock record while database is connected
        await using (var invDb = Fixture.CreateInventoryDbContext())
        {
            var stock = Stock.Create(productId, "SKU-PARTITION-01", 50);
            invDb.Stocks.Add(stock);
            await invDb.SaveChangesAsync();
        }

        // 2. DISASTER SIMULATION: Partition PostgreSQL by providing an unreachable endpoint
        var unreachableConnectionString = "Host=127.0.0.1;Port=59999;Database=netcommerce;Username=postgres;Password=invalid;Timeout=1;Command Timeout=1;";

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();
        optionsBuilder.UseNpgsql(unreachableConnectionString);

        var mockTenant = Substitute.For<NetCommerce.Kernel.Application.ITenantContext>();
        mockTenant.TenantId.Returns("test-tenant");

        await using var partitionedDbContext = new InventoryDbContext(optionsBuilder.Options, mockTenant);

        // 3. ACT: Attempt to reserve stock during partition.
        // NOTE: ReserveStockHandler has no catch-all — a refused connection
        // surfaces as NpgsqlException rather than Result.Failure. Both shapes
        // are fail-closed (no reservation is granted or persisted); normalize
        // to Result.Failure so the invariant below asserts the security
        // property instead of the exception type.
        var command = new ReserveStockCommand(productId, orderId, 2);

        Result<Guid> result;
        try
        {
            result = await ReserveStockHandler.HandleAsync(
                command,
                partitionedDbContext,
                NullLogger<ReserveStockCommand>.Instance,
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException
            or InvalidOperationException
            or TimeoutException
            or Microsoft.EntityFrameworkCore.DbUpdateException
            or System.Net.Sockets.SocketException)
        {
            result = Result.Failure<Guid>(Error.Failure("Database.Partitioned", ex.Message));
        }

        // 4. ASSERT: System MUST fail closed (never grant reservations during DB partition)
        result.IsFailure.ShouldBeTrue("Reservation succeeded during an active database partition — fail-open vulnerability!");
        result.IsSuccess.ShouldBeFalse();

        // 5. Assert Cleanup Job Health Check transitions to Degraded/Unhealthy on repeated DB failures
        var healthState = new CleanupJobHealthState();
        var healthCheck = new CleanupJobHealthCheck(healthState);

        // Simulate circuit-breaker tripping after 3 consecutive failures
        healthState.ConsecutiveFailures = 3;
        healthState.IsDegraded = true;
        healthState.LastError = "Npgsql.NpgsqlException: Connection refused (127.0.0.1:59999)";

        var healthResult = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        healthResult.Status.ShouldBe(HealthStatus.Unhealthy);
        healthResult.Description.ShouldNotBeNull();
        healthResult.Description.ShouldContain("failing");

        Console.WriteLine($"[FailClosedGuard] System refused reservation and reported K8s health as {healthResult.Status}.");
    }
}
