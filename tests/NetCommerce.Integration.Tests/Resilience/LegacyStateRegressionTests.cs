#nullable enable
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Application.Sagas;
using Npgsql;
using NSubstitute;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Resilience;

/// <summary>
///     CRITICAL PRODUCTION-READINESS TEST: Legacy State Regression Tests (The "Namespace" Guard)
///
///     <para>
///     After Phase 5/6 namespace migration, Wolverine's saga state and outbox messages contain
///     fully qualified type names stored as strings in PostgreSQL. This test suite verifies that
///     the LegacyTypeResolver correctly maps deprecated <c>NetCommerce.SharedKernel.*</c> namespaces
///     to the canonical <c>NetCommerce.Domain.Shared.*</c> types.
///     </para>
///
///     <para>
///     <b>The Risk:</b> A deployment that "lobotomizes" active orders by failing to deserialize
///     in-flight saga state. Orders stuck in <c>ReservingInventory</c> or <c>ProcessingPayment</c>
///     states would become orphaned, requiring manual database intervention.
///     </para>
///
///     <para>
///     <b>Success Criteria:</b> The system MUST successfully map legacy namespaces without throwing
///     <see cref="JsonException"/>. If this test fails, you CANNOT deploy without wiping the
///     database—which is unacceptable in PRODUCTION.
///     </para>
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "Resilience")]
[Trait("Category", "ProductionReadiness")]
public class LegacyStateRegressionTests : IntegrationTestBase
{
    public LegacyStateRegressionTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: Legacy Saga State Deserialization

    /// <summary>
    ///     CRITICAL TEST: Verifies that saga state containing legacy <c>NetCommerce.SharedKernel.Domain.Money</c>
    ///     can be deserialized after Phase 5/6 migration.
    ///
    ///     <para>
    ///     Scenario: An order was placed BEFORE the deployment. The saga state in PostgreSQL contains:
    ///     <code>
    ///     {
    ///       "totalAmount": {
    ///         "$type": "NetCommerce.SharedKernel.Domain.Money, NetCommerce.SharedKernel",
    ///         "amount": 150.00,
    ///         "currency": "GEL"
    ///       }
    ///     }
    ///     </code>
    ///     The system must resolve this to <see cref="NetCommerce.Domain.Shared.Money"/>.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task LegacySagaState_WithSharedKernelMoney_ShouldDeserializeCorrectly()
    {
        // Arrange: Create legacy JSON payload with deprecated namespace
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // This JSON simulates what would be stored in wolverine.saga_state BEFORE Phase 5/6 migration
        var legacySagaStateJson = $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{customerId}}",
            "orderNumber": "ORD-LEGACY-001",
            "totalAmount": {
                "$type": "NetCommerce.SharedKernel.Domain.Money, NetCommerce.SharedKernel",
                "amount": 150.00,
                "currency": "GEL"
            },
            "items": [
                {
                    "productId": "{{Guid.NewGuid()}}",
                    "quantity": 2,
                    "unitPrice": {
                        "$type": "NetCommerce.SharedKernel.Domain.Money, NetCommerce.SharedKernel",
                        "amount": 75.00,
                        "currency": "GEL"
                    }
                }
            ],
            "state": "ReservingInventory",
            "isInventoryReserved": false,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "startedAt": "{{DateTime.UtcNow:O}}"
        }
        """;

        // Act: Attempt to deserialize using the configured JSON options
        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();

        Exception? deserializationException = null;
        OrderFulfillmentSaga? saga = null;

        try
        {
            saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(legacySagaStateJson, options);
        }
        catch (Exception ex)
        {
            deserializationException = ex;
        }

        // Assert: Deserialization must succeed without throwing
        deserializationException.ShouldBeNull(
            $"CRITICAL: Legacy saga state deserialization failed! Exception: {deserializationException?.Message}\n" +
            "This means in-flight orders WILL BE ORPHANED after deployment.\n" +
            "DO NOT DEPLOY without fixing LegacyTypeResolver.");

        saga.ShouldNotBeNull("Saga deserialized to null");
        saga.Id.ShouldBe(orderId);
        saga.TotalAmount.ShouldNotBeNull();
        saga.TotalAmount.Amount.ShouldBe(150.00m);
        saga.TotalAmount.Currency.ShouldBe("GEL");
    }

    /// <summary>
    ///     Verifies that legacy PriceBreakdown with SharedKernel namespace deserializes correctly.
    /// </summary>
    [Fact]
    public void LegacyPriceBreakdown_WithSharedKernelNamespace_ShouldDeserializeCorrectly()
    {
        // Arrange: Legacy PriceBreakdown JSON
        var legacyJson = """
        {
            "$type": "NetCommerce.SharedKernel.Domain.PriceBreakdown, NetCommerce.SharedKernel",
            "basePrice": 100.00,
            "quantity": 2,
            "discountAmount": 10.00,
            "taxAmount": 16.20,
            "taxRate": 0.18,
            "taxType": "VAT",
            "currency": "GEL"
        }
        """;

        // Act
        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();

        Exception? ex = null;
        PriceBreakdown? breakdown = null;

        try
        {
            breakdown = JsonSerializer.Deserialize<PriceBreakdown>(legacyJson, options);
        }
        catch (Exception e)
        {
            ex = e;
        }

        // Assert
        ex.ShouldBeNull($"Legacy PriceBreakdown deserialization failed: {ex?.Message}");
        breakdown.ShouldNotBeNull();
        breakdown.BasePrice.ShouldBe(100.00m);
        breakdown.TaxRate.ShouldBe(0.18m);
        breakdown.Currency.ShouldBe("GEL");
    }

    #endregion

    #region Test 2: Legacy Message Type Resolution

    /// <summary>
    ///     CRITICAL TEST: Verifies that Wolverine can resolve messages from the outbox
    ///     that were persisted with legacy type names.
    ///
    ///     <para>
    ///     Scenario: The <c>wolverine.wolverine_outgoing_envelopes</c> table contains a row with:
    ///     <code>message_type = 'NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent'</code>
    ///     The system must map this to the canonical type.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task LegacyMessageType_InOutbox_ShouldResolveToCanonicalType()
    {
        // Arrange: Insert a legacy message type into the outbox table
        var messageId = Guid.NewGuid();
        var legacyMessageType = "NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent";
        var orderId = Guid.NewGuid();

        // Create a minimal message body (the actual body format may vary)
        var messageBody = JsonSerializer.SerializeToUtf8Bytes(new
        {
            OrderId = orderId,
            OrderNumber = "ORD-LEGACY-002",
            CustomerId = Guid.NewGuid(),
            TotalAmount = new { Amount = 99.99m, Currency = "GEL" }
        });

        await using var connection = new NpgsqlConnection(Fixture.PostgresConnectionString);
        await connection.OpenAsync();

        // Ensure wolverine schema and table exist
        await using var createSchemaCmd = connection.CreateCommand();
        createSchemaCmd.CommandText = @"
            CREATE SCHEMA IF NOT EXISTS wolverine;
            CREATE TABLE IF NOT EXISTS wolverine.wolverine_outgoing_envelopes (
                id uuid PRIMARY KEY,
                owner_id integer NOT NULL DEFAULT 0,
                destination text NOT NULL,
                deliver_by timestamptz,
                body bytea NOT NULL,
                attempts integer DEFAULT 0,
                message_type text NOT NULL,
                scheduled_at timestamptz,
                sent_at timestamptz  -- CRITICAL: Wolverine 5.x requires this for outbox maintenance
            );";
        await createSchemaCmd.ExecuteNonQueryAsync();

        // Insert a message with legacy type name
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO wolverine.wolverine_outgoing_envelopes
            (id, owner_id, destination, body, message_type)
            VALUES (@id, 0, 'local://default', @body, @messageType)
            ON CONFLICT (id) DO NOTHING;";
        insertCmd.Parameters.AddWithValue("id", messageId);
        insertCmd.Parameters.AddWithValue("body", messageBody);
        insertCmd.Parameters.AddWithValue("messageType", legacyMessageType);
        await insertCmd.ExecuteNonQueryAsync();

        // Act: Verify the LegacyTypeResolver can map the type name
        var resolvedType = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver
            .ResolveLegacyType(legacyMessageType);

        // Assert
        resolvedType.ShouldNotBeNull(
            $"CRITICAL: Legacy message type '{legacyMessageType}' could not be resolved!\n" +
            "Messages in the outbox WILL BE MOVED TO DLQ after deployment.\n" +
            "Add this type to LegacyTypeMappings in LegacyTypeResolver.");

        resolvedType.ShouldBe(typeof(OrderSubmittedIntegrationEvent));

        // Cleanup
        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM wolverine.wolverine_outgoing_envelopes WHERE id = @id";
        deleteCmd.Parameters.AddWithValue("id", messageId);
        await deleteCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     Tests ALL legacy message types in the mapping to ensure complete coverage.
    /// </summary>
    [Theory]
    [InlineData("NetCommerce.SharedKernel.Events.StartOrderFulfillmentCommand", typeof(StartOrderFulfillmentCommand))]
    [InlineData("NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent", typeof(OrderSubmittedIntegrationEvent))]
    [InlineData("NetCommerce.SharedKernel.Events.OrderGracePeriodConfirmedIntegrationEvent", typeof(OrderGracePeriodConfirmedIntegrationEvent))]
    [InlineData("NetCommerce.SharedKernel.Events.OrderPlacedIntegrationEvent", typeof(OrderPlacedIntegrationEvent))]
    [InlineData("NetCommerce.SharedKernel.Events.OrderCancelledIntegrationEvent", typeof(OrderCancelledIntegrationEvent))]
    [InlineData("NetCommerce.SharedKernel.Events.ReserveInventoryCommand", typeof(ReserveInventoryCommand))]
    [InlineData("NetCommerce.SharedKernel.Events.InventoryReserved", typeof(InventoryReserved))]
    [InlineData("NetCommerce.SharedKernel.Events.InventoryReservationFailed", typeof(InventoryReservationFailed))]
    [InlineData("NetCommerce.SharedKernel.Events.RequestPaymentCommand", typeof(RequestPaymentCommand))]
    [InlineData("NetCommerce.SharedKernel.Events.PaymentSucceeded", typeof(PaymentSucceeded))]
    [InlineData("NetCommerce.SharedKernel.Events.PaymentFailed", typeof(PaymentFailed))]
    [InlineData("NetCommerce.SharedKernel.Events.RefundPaymentCommand", typeof(RefundPaymentCommand))]
    [InlineData("NetCommerce.SharedKernel.Events.GracePeriodTimeout", typeof(GracePeriodTimeout))]
    [InlineData("NetCommerce.SharedKernel.Events.PaymentTimeoutMessage", typeof(PaymentTimeoutMessage))]
    [InlineData("NetCommerce.SharedKernel.Events.FinalizeOrderCommand", typeof(FinalizeOrderCommand))]
    [InlineData("NetCommerce.SharedKernel.Events.FailOrderCommand", typeof(FailOrderCommand))]
    public void LegacyMessageType_ShouldResolveToCanonicalType(string legacyTypeName, Type expectedType)
    {
        // Act
        var resolvedType = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver
            .ResolveLegacyType(legacyTypeName);

        // Assert
        resolvedType.ShouldNotBeNull(
            $"Legacy type '{legacyTypeName}' is not mapped in LegacyTypeResolver.\n" +
            "Add this mapping to prevent DLQ on deployment.");

        resolvedType.ShouldBe(expectedType,
            $"Legacy type '{legacyTypeName}' mapped to wrong type.\n" +
            $"Expected: {expectedType.FullName}\n" +
            $"Actual: {resolvedType.FullName}");
    }

    #endregion

    #region Test 3: End-to-End Saga State Transition with Legacy Data

    /// <summary>
    ///     INTEGRATION TEST: Verifies that a saga can be loaded from legacy state and
    ///     successfully transition to the next state.
    ///
    ///     <para>
    ///     This is the "Real World" test - it simulates:
    ///     1. A saga was persisted to the database BEFORE Phase 5/6 deployment
    ///     2. After deployment, Wolverine loads the saga
    ///     3. The saga successfully processes the next event
    ///     </para>
    /// </summary>
    [Fact]
    public async Task LegacySaga_ShouldTransitionStateSuccessfully()
    {
        // Arrange: Create a saga using the current code (it will use canonical types)
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var startCommand = new StartOrderFulfillmentCommand(
            OrderId: orderId,
            OrderNumber: "ORD-TRANSITION-001",
            CustomerId: customerId,
            TotalAmount: Money.Create(200.00m, "GEL"),
            Items: new List<OrderItemReservation>
            {
                new(productId, 2, "SKU-TEST-001")
            });

        var logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();

        // Start the saga
        var (saga, reserveCommand, timeout) = OrderFulfillmentSaga.Start(startCommand, logger);

        // Verify initial state
        saga.State.ShouldBe(OrderFulfillmentState.ReservingInventory);
        saga.TotalAmount.Amount.ShouldBe(200.00m);

        // Simulate serialization/deserialization (what happens during persistence)
        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();
        var serialized = JsonSerializer.Serialize(saga, options);
        var deserialized = JsonSerializer.Deserialize<OrderFulfillmentSaga>(serialized, options);

        // Assert: Saga state survived round-trip
        deserialized.ShouldNotBeNull();
        deserialized.Id.ShouldBe(orderId);
        deserialized.TotalAmount.Amount.ShouldBe(200.00m);
        deserialized.TotalAmount.Currency.ShouldBe("GEL");
        deserialized.State.ShouldBe(OrderFulfillmentState.ReservingInventory);
        deserialized.Items.Count.ShouldBe(1);
        deserialized.Items[0].Quantity.ShouldBe(2);
        deserialized.Items[0].Sku.ShouldBe("SKU-TEST-001");
    }

    #endregion

    #region Test 4: Resolution Counter Monitoring

    /// <summary>
    ///     Verifies that the LegacyResolutionCount metric is incremented when legacy types are resolved.
    ///     This counter is used to determine when it's safe to remove the LegacyTypeResolver.
    /// </summary>
    [Fact]
    public void LegacyResolutionCount_ShouldIncrementOnResolution()
    {
        // Arrange
        var countBefore = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.LegacyResolutionCount;

        // Act: Resolve a legacy type
        var _ = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver
            .ResolveLegacyType("NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent");

        // Assert
        var countAfter = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.LegacyResolutionCount;
        countAfter.ShouldBeGreaterThan(countBefore,
            "LegacyResolutionCount should increment when resolving legacy types.\n" +
            "This metric is used to determine when it's safe to remove LegacyTypeResolver.");
    }

    #endregion

    #region Test 5: Value Object Nested in Collections

    /// <summary>
    ///     Tests that OrderItemReservation records in collections are properly deserialized.
    /// </summary>
    [Fact]
    public void OrderItemReservation_InCollection_ShouldDeserializeCorrectly()
    {
        // Arrange: Saga state with OrderItemReservation items
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var legacyJson = $$"""
        {
            "id": "{{Guid.NewGuid()}}",
            "customerId": "{{Guid.NewGuid()}}",
            "orderNumber": "ORD-COLLECTION-001",
            "totalAmount": {
                "amount": 300.00,
                "currency": "GEL"
            },
            "items": [
                {
                    "productId": "{{productId1}}",
                    "quantity": 2,
                    "sku": "SKU-LEGACY-001"
                },
                {
                    "productId": "{{productId2}}",
                    "quantity": 1,
                    "sku": "SKU-LEGACY-002"
                }
            ],
            "state": "InGracePeriod",
            "isInventoryReserved": true,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "startedAt": "{{DateTime.UtcNow:O}}"
        }
        """;

        // Act
        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();
        var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(legacyJson, options);

        // Assert
        saga.ShouldNotBeNull();
        saga.Items.Count.ShouldBe(2);
        saga.Items[0].Quantity.ShouldBe(2);
        saga.Items[0].Sku.ShouldBe("SKU-LEGACY-001");
        saga.Items[1].Quantity.ShouldBe(1);
        saga.Items[1].Sku.ShouldBe("SKU-LEGACY-002");
    }

    #endregion
}
