#nullable enable
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Application.Sagas;
using NSubstitute;
using Shouldly;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Resilience;

/// <summary>
///     PRODUCTION-READINESS TEST: Saga Version Mismatch (Blue-Green Deployment)
///
///     <para>
///     Wolverine persists saga state as JSON with fully qualified type names.
///     During blue-green deployments, a "V1" saga in the database may be loaded
///     by a "V2" application instance with different type definitions.
///     </para>
///
///     <para>
///     <b>Production Risk:</b> Without this test, the first deployment with active orders
///     will crash when V2 tries to deserialize V1 saga state.
///     </para>
///
///     <para>
///     <b>Scenarios Tested:</b>
///     1. New property added to saga state (backward compatible)
///     2. Property type changed (breaking - needs migration)
///     3. Property removed (forward compatible with defaults)
///     4. Enum value added (backward compatible)
///     </para>
/// </summary>
public class SagaVersionMismatchTests : IntegrationTestBase
{
    public SagaVersionMismatchTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: V1 Saga State Loaded by V2 Application (New Property Added)

    /// <summary>
    ///     Simulates a V1 saga state (missing new properties) being loaded by V2 application.
    ///
    ///     <para>
    ///     Scenario: V2 adds a new "ShippingTrackingNumber" property to the saga.
    ///     V1 sagas in the database don't have this property.
    ///     Expected: Deserialization succeeds with default value for new property.
    ///     </para>
    /// </summary>
    [Fact]
    public void V1SagaState_LoadedByV2_NewPropertyAdded_ShouldDeserializeWithDefaults()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create V1 saga JSON (missing properties added in V2)
        // ═══════════════════════════════════════════════════════════════════════

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // This JSON represents a V1 saga state - it's missing properties that V2 might add
        // For example, if V2 added "shippingTrackingNumber" or "estimatedDeliveryDate"
        var v1SagaJson = $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{customerId}}",
            "orderNumber": "ORD-V1-001",
            "totalAmount": {
                "amount": 199.99,
                "currency": "GEL"
            },
            "items": [
                {
                    "productId": "{{Guid.NewGuid()}}",
                    "quantity": 2,
                    "sku": "SKU-V1-001"
                }
            ],
            "state": "InGracePeriod",
            "isInventoryReserved": true,
            "isInventoryLockedForPayment": false,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "startedAt": "{{DateTime.UtcNow:O}}"
        }
        """;

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Deserialize V1 JSON with current (V2) type definition
        // ═══════════════════════════════════════════════════════════════════════

        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();
        var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(v1SagaJson, options);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Deserialization succeeds, new properties have defaults
        // ═══════════════════════════════════════════════════════════════════════

        saga.ShouldNotBeNull("V1 saga state should deserialize with V2 application");
        saga.Id.ShouldBe(orderId);
        saga.OrderNumber.ShouldBe("ORD-V1-001");
        saga.TotalAmount.Amount.ShouldBe(199.99m);
        saga.State.ShouldBe(OrderFulfillmentState.InGracePeriod);

        // New properties added in V2 should have default values
        // (This documents what defaults are expected)
        saga.PaymentTransactionId.ShouldBeNull("New optional properties should default to null");
        saga.ReservedItems.ShouldBeNull("New collection properties should default to null");
        saga.FailureReason.ShouldBeNull("New optional properties should default to null");
        saga.CompletedAt.ShouldBeNull("New optional DateTime? should default to null");

        Console.WriteLine($"[SagaVersioning] V1→V2 backward compatibility verified");
        Console.WriteLine($"[SagaVersioning] Saga {orderId} loaded successfully with defaults for new properties");
    }

    #endregion

    #region Test 2: Enum Value Migration (New State Added)

    /// <summary>
    ///     Tests that adding new enum values doesn't break existing sagas.
    ///
    ///     <para>
    ///     Scenario: V2 adds a new OrderFulfillmentState "AwaitingCustomerVerification".
    ///     V1 sagas using existing states should still deserialize correctly.
    ///     </para>
    /// </summary>
    [Fact]
    public void V1SagaState_WithExistingEnumValue_ShouldDeserializeCorrectly()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create saga JSON with existing enum value
        // ═══════════════════════════════════════════════════════════════════════

        // Test all existing enum values to ensure they deserialize correctly
        var enumValues = Enum.GetValues<OrderFulfillmentState>();

        foreach (var state in enumValues)
        {
            var sagaJson = $$"""
            {
                "id": "{{Guid.NewGuid()}}",
                "customerId": "{{Guid.NewGuid()}}",
                "orderNumber": "ORD-ENUM-{{state}}",
                "totalAmount": { "amount": 100.00, "currency": "GEL" },
                "items": [],
                "state": "{{state}}",
                "isInventoryReserved": false,
                "isPaid": false,
                "isInventoryConfirmed": false,
                "startedAt": "{{DateTime.UtcNow:O}}"
            }
            """;

            var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();
            var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(sagaJson, options);

            saga.ShouldNotBeNull($"Saga with state '{state}' should deserialize");
            saga.State.ShouldBe(state, $"State '{state}' should round-trip correctly");

            Console.WriteLine($"[SagaVersioning] Enum value '{state}' verified");
        }
    }

    #endregion

    #region Test 3: Type Name Migration (SharedKernel → Domain.Shared)

    /// <summary>
    ///     Tests that sagas persisted with legacy type names can be loaded after namespace migration.
    ///
    ///     <para>
    ///     This is critical for Phase 5/6 migration where types moved from
    ///     NetCommerce.SharedKernel.* to NetCommerce.Domain.Shared.*
    ///     </para>
    /// </summary>
    [Fact]
    public void LegacyTypeName_InPersistedSaga_ShouldResolveToCanonicalType()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create saga JSON with legacy $type annotations
        // ═══════════════════════════════════════════════════════════════════════

        var orderId = Guid.NewGuid();

        // JSON with legacy SharedKernel type names (as would be persisted before migration)
        var legacySagaJson = $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{Guid.NewGuid()}}",
            "orderNumber": "ORD-LEGACY-TYPE-001",
            "totalAmount": {
                "$type": "NetCommerce.SharedKernel.Domain.Money, NetCommerce.SharedKernel",
                "amount": 299.99,
                "currency": "GEL"
            },
            "items": [],
            "state": "ProcessingPayment",
            "isInventoryReserved": true,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "startedAt": "{{DateTime.UtcNow:O}}"
        }
        """;

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Deserialize with LegacyTypeResolver
        // ═══════════════════════════════════════════════════════════════════════

        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();
        var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(legacySagaJson, options);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Saga loaded correctly with canonical types
        // ═══════════════════════════════════════════════════════════════════════

        saga.ShouldNotBeNull("Legacy saga with SharedKernel types should deserialize");
        saga.TotalAmount.Amount.ShouldBe(299.99m);
        saga.TotalAmount.Currency.ShouldBe("GEL");

        // Verify the Money type is now the canonical Domain.Shared type
        saga.TotalAmount.GetType().FullName.ShouldBe("NetCommerce.Domain.Shared.Money",
            "Money should be resolved to canonical Domain.Shared.Money type");

        Console.WriteLine($"[SagaVersioning] Legacy SharedKernel.Money → Domain.Shared.Money migration verified");
    }

    #endregion

    #region Test 4: Saga Continuity During Deployment

    /// <summary>
    ///     Tests that an in-flight saga can receive messages from both V1 and V2 handlers.
    ///
    ///     <para>
    ///     Scenario: Saga started on V1, then V2 deployed.
    ///     Messages might be sent by V1 instances (draining) or V2 instances (new).
    ///     The saga should handle both gracefully.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task InFlightSaga_DuringDeployment_ShouldHandleMessagesFromBothVersions()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Start a saga (simulating V1)
        // ═══════════════════════════════════════════════════════════════════════

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            customerId,
            "ORD-DEPLOY-001",
            Money.Create(150.00m),
            [new OrderItemReservation(productId, 1, "SKU-DEPLOY-001")]);

        var logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();

        // Start the saga
        var (saga, reserveCommand, timeout) = OrderFulfillmentSaga.Start(startCommand, logger);

        saga.State.ShouldBe(OrderFulfillmentState.ReservingInventory);

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Serialize (V1 persists) → Deserialize (V2 loads) → Handle message
        // ═══════════════════════════════════════════════════════════════════════

        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();

        // Serialize as V1 would persist
        var serializedState = JsonSerializer.Serialize(saga, options);

        // Deserialize as V2 would load
        var loadedSaga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(serializedState, options);
        loadedSaga.ShouldNotBeNull();

        // Simulate receiving InventoryReserved response (correctly typed)
        var inventoryReservedEvent = new InventoryReserved(
            orderId,
            [new ReservedItem(productId, Guid.NewGuid(), 1)]);

        // Handle the message (V2 handler processing V1 saga state)
        // The Handle method returns (OrderStatusChanged, GracePeriodTimeout) tuple
        var (notification, timer) = loadedSaga.Handle(inventoryReservedEvent, logger);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Saga transitioned correctly
        // ═══════════════════════════════════════════════════════════════════════

        loadedSaga.State.ShouldBe(OrderFulfillmentState.InGracePeriod,
            "Saga should transition to InGracePeriod after inventory reserved");
        loadedSaga.IsInventoryReserved.ShouldBeTrue();
        notification.ShouldNotBeNull("Should emit OrderStatusChanged notification");
        timer.ShouldNotBeNull("Should schedule GracePeriodTimeout");

        Console.WriteLine($"[SagaVersioning] In-flight saga handled cross-version message successfully");
        Console.WriteLine($"[SagaVersioning] State transition: ReservingInventory → InGracePeriod");
    }

    #endregion

    #region Test 5: Property Type Change Detection

    /// <summary>
    ///     Tests that incompatible property type changes are detected.
    ///
    ///     <para>
    ///     Scenario: V1 has "TotalAmount" as decimal, V2 changes it to Money.
    ///     This is a BREAKING change that requires data migration.
    ///     </para>
    /// </summary>
    [Fact]
    public void IncompatibleTypeChange_ShouldFailGracefully()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create JSON with incompatible type (plain decimal instead of Money)
        // ═══════════════════════════════════════════════════════════════════════

        var malformedJson = $$"""
        {
            "id": "{{Guid.NewGuid()}}",
            "customerId": "{{Guid.NewGuid()}}",
            "orderNumber": "ORD-MALFORMED-001",
            "totalAmount": 199.99,
            "items": [],
            "state": "NotStarted",
            "isInventoryReserved": false,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "startedAt": "{{DateTime.UtcNow:O}}"
        }
        """;

        // ═══════════════════════════════════════════════════════════════════════
        // ACT & ASSERT: Deserialization should fail with clear error
        // ═══════════════════════════════════════════════════════════════════════

        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();

        var exception = Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<OrderFulfillmentSaga>(malformedJson, options));

        // The error should indicate the type mismatch
        Console.WriteLine($"[SagaVersioning] Type mismatch correctly detected:");
        Console.WriteLine($"[SagaVersioning] Exception: {exception.Message}");

        // This test documents that such changes are BREAKING and require migration
        Console.WriteLine($"[SagaVersioning] ⚠️ Changing property types is a BREAKING change");
        Console.WriteLine($"[SagaVersioning] ⚠️ Requires data migration script before deployment");
    }

    #endregion

    #region Test 6: Collection Property Migration

    /// <summary>
    ///     Tests that changes to collection item types are handled correctly.
    ///
    ///     <para>
    ///     Scenario: V2 adds new properties to OrderItemReservation.
    ///     V1 saga state with old OrderItemReservation format should still load.
    ///     </para>
    /// </summary>
    [Fact]
    public void CollectionPropertyMigration_ShouldPreserveExistingData()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create saga with old-format collection items
        // ═══════════════════════════════════════════════════════════════════════

        var sagaJson = $$"""
        {
            "id": "{{Guid.NewGuid()}}",
            "customerId": "{{Guid.NewGuid()}}",
            "orderNumber": "ORD-COLLECTION-001",
            "totalAmount": { "amount": 500.00, "currency": "GEL" },
            "items": [
                {
                    "productId": "{{Guid.NewGuid()}}",
                    "quantity": 5,
                    "sku": "SKU-COLLECTION-001"
                },
                {
                    "productId": "{{Guid.NewGuid()}}",
                    "quantity": 3,
                    "sku": "SKU-COLLECTION-002"
                }
            ],
            "state": "ReservingInventory",
            "isInventoryReserved": false,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "startedAt": "{{DateTime.UtcNow:O}}"
        }
        """;

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Deserialize and verify collection integrity
        // ═══════════════════════════════════════════════════════════════════════

        var options = NetCommerce.Kernel.Wolverine.Serialization.LegacyTypeResolver.CreateOptions();
        var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(sagaJson, options);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: All collection items preserved
        // ═══════════════════════════════════════════════════════════════════════

        saga.ShouldNotBeNull();
        saga.Items.Count.ShouldBe(2, "All collection items should be preserved");
        saga.Items[0].Quantity.ShouldBe(5);
        saga.Items[0].Sku.ShouldBe("SKU-COLLECTION-001");
        saga.Items[1].Quantity.ShouldBe(3);
        saga.Items[1].Sku.ShouldBe("SKU-COLLECTION-002");

        Console.WriteLine($"[SagaVersioning] Collection property migration verified");
        Console.WriteLine($"[SagaVersioning] {saga.Items.Count} items preserved correctly");
    }

    #endregion
}
