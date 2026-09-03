#nullable enable

using System.Diagnostics;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using Npgsql;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Resilience;

[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "OutboxResilience")]
public sealed class WolverineOutboxPruningAndWalPressureTests : IntegrationTestBase
{
    public WolverineOutboxPruningAndWalPressureTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task WolverineOutbox_UnderSustainedThroughput_MustPruneProcessedEnvelopes()
    {
        const int messageBatchCount = 200;
        var productId = Guid.NewGuid();
        var sku = $"SKU-PRUNE-{Guid.NewGuid():N}";

        await using (var invDb = Fixture.CreateInventoryDbContext())
        {
            var stock = Stock.Create(productId, sku, 100_000);
            invDb.Stocks.Add(stock);
            await invDb.SaveChangesAsync();
        }

        // Sample initial WAL metrics from PostgreSQL system catalog
        var walBytesBefore = await GetWalBytesWrittenAsync();

        // 1. Dispatch a sustained wave of transactional messages.
        // Bounded parallelism: the Testcontainer PostgreSQL defaults to
        // max_connections=100, so unbounded fan-out would exhaust server
        // connections (53300) instead of measuring outbox/WAL behavior.
        Func<IMessageContext, Task> batchAction = async bus =>
        {
            using var throttle = new SemaphoreSlim(initialCount: 16, maxCount: 16);
            var tasks = Enumerable.Range(0, messageBatchCount).Select(async i =>
            {
                await throttle.WaitAsync();
                try
                {
                    var orderId = Guid.NewGuid();
                    var command = new ReserveInventoryCommand(
                        orderId,
                        [new OrderItemReservation(productId, 1, sku)]);

                    await bus.InvokeAsync(command);
                }
                finally
                {
                    throttle.Release();
                }
            });

            await Task.WhenAll(tasks);
        };

        var session = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(batchAction);

        session.AllExceptions().ShouldBeEmpty();

        // 2. Sample outbox envelope count immediately after execution
        var incomingCount = await GetTableCountAsync("wolverine.wolverine_incoming_envelopes");
        var outgoingCount = await GetTableCountAsync("wolverine.wolverine_outgoing_envelopes");

        Console.WriteLine($"[OUTBOX PRUNING] Immediate Envelope State -> Incoming: {incomingCount}, Outgoing: {outgoingCount}");

        // 3. Allow Wolverine's durability agent sweeper to run pruning cycle (default interval: 5-10s)
        var sw = Stopwatch.StartNew();
        var pruned = false;

        while (sw.Elapsed < TimeSpan.FromSeconds(25))
        {
            var currentIncoming = await GetTableCountAsync("wolverine.wolverine_incoming_envelopes");
            var currentOutgoing = await GetTableCountAsync("wolverine.wolverine_outgoing_envelopes");

            if (currentIncoming == 0 && currentOutgoing == 0)
            {
                pruned = true;
                break;
            }

            await Task.Delay(1000);
        }

        Console.WriteLine($"[OUTBOX PRUNING] Post-Drain Envelope State -> Pruned Cleanly: {pruned} in {sw.Elapsed.TotalSeconds:F1}s");

        pruned.ShouldBeTrue(
            "Wolverine outbox tables failed to prune processed messages within the SLA window, leading to long-term table bloat.");

        // 4. Validate WAL generation volume is bounded
        var walBytesAfter = await GetWalBytesWrittenAsync();
        var totalWalMb = (walBytesAfter - walBytesBefore) / (1024.0 * 1024.0);

        Console.WriteLine($"[WAL PRESSURE] Total WAL bytes generated for {messageBatchCount} transactions: {totalWalMb:F2} MB");
        totalWalMb.ShouldBeGreaterThan(0);
        totalWalMb.ShouldBeLessThan(100, "Excessive WAL generation detected; transaction volume is causing extreme disk write amplification.");
    }

    private async Task<long> GetTableCountAsync(string qualifiedTableName)
    {
        await using var connection = new NpgsqlConnection(Fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
#pragma warning disable CA2100 // Table name is a hardcoded wolverine envelope table, never user input
        cmd.CommandText = $"SELECT COUNT(*) FROM {qualifiedTableName};";
#pragma warning restore CA2100
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> GetWalBytesWrittenAsync()
    {
        await using var connection = new NpgsqlConnection(Fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        // pg_stat_wal is standard in PostgreSQL 14+
        cmd.CommandText = "SELECT wal_bytes FROM pg_stat_wal;";
        var result = await cmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0L;
    }
}
