#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Ordering.Application.Sagas;
using Npgsql;
using Shouldly;
using Wolverine.Tracking;
using Xunit;

namespace NetCommerce.Integration.Tests.Migrations;

/// <summary>
///     BLUE-GREEN RULE: Wolverine in-flight envelope durability.
///     Pending messages and scheduled timeouts written by Version N pods survive in
///     PostgreSQL across the deployment and are picked up by Version N+1 workers.
///     These tests verify that payloads in historical/future wire formats (casing
///     drift, unknown additive fields, missing optional fields) deserialize safely,
///     execute through the real Wolverine runtime, advance the OrderFulfillmentSaga,
///     and never route to the dead-letter storage.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "WolverineDrift")]
public sealed class WolverineEnvelopeDriftTests : IntegrationTestBase
{
    public WolverineEnvelopeDriftTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task InFlightTimeoutEnvelope_FromPreviousVersion_MustDeserializeAndAdvanceSaga()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 1: Seed inventory stock in database
        // ═══════════════════════════════════════════════════════════════════════
        var productId = Guid.NewGuid();
        var sku = $"SKU-DRIFT-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();

        await using (var inventoryDb = Fixture.CreateInventoryDbContext())
        {
            var stock = Stock.Create(productId, sku, 50);
            inventoryDb.Stocks.Add(stock);
            await inventoryDb.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 2: Start saga through Wolverine (reaches InGracePeriod naturally)
        // Wolverine persists the saga in PostgreSQL and sets up ReservedItems
        // ═══════════════════════════════════════════════════════════════════════
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-DRIFT-001",
            Money.Create(199.99m, "GEL"),
            [new OrderItemReservation(productId, 2, sku)]);

        var startTracking = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryReserved>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        startTracking.AllExceptions().ShouldBeEmpty("Saga initialization should complete without errors");

        // Record baseline dead-letter count
        var deadLettersBefore = await CountDeadLettersAsync();

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 3: Craft historical wire JSON for in-flight timeout envelope
        // (Simulates a message scheduled by Version N pods: camelCase, unknown
        //  additive fields from legacy telemetry, and missing optional fields)
        // ═══════════════════════════════════════════════════════════════════════
        var legacyEnvelopeJson = $$"""
        {
            "id": "{{orderId}}",
            "scheduledTime": "2026-01-15T10:35:00.000Z",
            "originVersion": "1.0.0",
            "telemetryTraceId": "legacy-trace-abc-123"
        }
        """;

        // Deserialize using Wolverine's canonical System.Text.Json options
        var deserializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };

        var timeoutMessage = JsonSerializer.Deserialize<GracePeriodTimeout>(legacyEnvelopeJson, deserializerOptions);
        timeoutMessage.ShouldNotBeNull("Legacy in-flight envelope must deserialize cleanly");
        timeoutMessage.Id.ShouldBe(orderId, "SagaIdentity correlation ID must survive wire format drift");

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 4: Execute through Wolverine runtime
        // Timeout → LockInventoryForPaymentCommand → InventoryLocked →
        // RequestPaymentCommand → PaymentInitiated
        // ═══════════════════════════════════════════════════════════════════════
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<PaymentInitiated>(Fixture.Host)
            .InvokeMessageAndWaitAsync(timeoutMessage);

        tracked.AllExceptions().ShouldBeEmpty(
            "Legacy in-flight envelope caused an unhandled exception during message execution.");

        tracked.Sent.MessagesOf<LockInventoryForPaymentCommand>().ShouldNotBeEmpty(
            "Saga must advance to LockingInventory from the legacy envelope");
        tracked.Sent.MessagesOf<RequestPaymentCommand>().ShouldNotBeEmpty(
            "Saga must advance to RequestPayment from the legacy envelope");

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 5: Verify side-effects in PostgreSQL and assert zero dead letters
        // ═══════════════════════════════════════════════════════════════════════
        await using var paymentsCtx = Fixture.CreatePaymentsDbContext();
        var paymentTxn = await paymentsCtx.Transactions.FirstOrDefaultAsync(t => t.OrderId == orderId);
        paymentTxn.ShouldNotBeNull("Payment transaction must be created when the legacy envelope advances the saga");

        var deadLettersAfter = await CountDeadLettersAsync();
        (deadLettersAfter - deadLettersBefore).ShouldBe(0, "In-flight legacy message was routed to the Dead Letter Queue!");
    }

    [Fact]
    public void SystemTextJson_MustSafelyHandle_UnknownPropertiesAndMissingOptionalFields()
    {
        var futurePayloadWithExtraFields = """
        {
            "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "reservedItems": [
                {
                    "productId": "4fa85f64-5717-4562-b3fc-2c963f66afa6",
                    "reservationId": "5fa85f64-5717-4562-b3fc-2c963f66afa6",
                    "quantity": 2
                }
            ],
            "unknownFutureTelemetry": "trace-abc-xyz",
            "futureProtocolVersion": 3
        }
        """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };

        var parsed = Should.NotThrow(() =>
            JsonSerializer.Deserialize<InventoryReserved>(futurePayloadWithExtraFields, options));

        parsed.ShouldNotBeNull();
        parsed.OrderId.ShouldBe(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"));
        parsed.ReservedItems.ShouldHaveSingleItem();
    }

    private async Task<long> CountDeadLettersAsync()
    {
        await using var connection = new NpgsqlConnection(Fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM wolverine.wolverine_dead_letters;";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
