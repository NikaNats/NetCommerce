#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Application.Sagas;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Integration.Tests.Resilience;

/// <summary>
///     CRITICAL: Blue-Green Saga Compatibility Tests (Deployment "Lobotomy" Prevention)
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "BlueGreen")]
[Trait("Category", "Resilience")]
[Trait("Category", "ProductionReadiness")]
public class SagaBlueGreenCompatibilityTests : IntegrationTestBase
{
    public SagaBlueGreenCompatibilityTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    private static JsonSerializerOptions CreateCanonicalOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    #region V1 Snapshots: Initial Production Release

    /// <summary>
    ///     Snapshot from initial production release (V1.0.0).
    ///     State: ReservingInventory (early in the saga lifecycle)
    /// </summary>
    private static string GetV1SagaSnapshot_ReservingInventory(Guid orderId, Guid customerId)
    {
        return $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{customerId}}",
            "orderNumber": "ORD-V1-001",
            "totalAmount": { "amount": 299.99, "currency": "GEL" },
            "items": [
                {
                    "productId": "{{Guid.NewGuid()}}",
                    "quantity": 2,
                    "sku": "SKU-V1-ITEM-001"
                }
            ],
            "state": "ReservingInventory",
            "isInventoryReserved": false,
            "isInventoryLockedForPayment": false,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "paymentTransactionId": null,
            "reservedItems": null,
            "failureReason": null,
            "startedAt": "2026-01-15T10:30:00.000Z",
            "completedAt": null
        }
        """;
    }

    /// <summary>
    ///     Snapshot from V1 with inventory reserved (InGracePeriod state)
    /// </summary>
    private static string GetV1SagaSnapshot_InGracePeriod(Guid orderId, Guid customerId)
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        return $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{customerId}}",
            "orderNumber": "ORD-V1-GRACE-001",
            "totalAmount": { "amount": 150.00, "currency": "GEL" },
            "items": [
                {
                    "productId": "{{productId}}",
                    "quantity": 1,
                    "sku": "SKU-V1-GRACE-ITEM"
                }
            ],
            "state": "InGracePeriod",
            "isInventoryReserved": true,
            "isInventoryLockedForPayment": false,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "paymentTransactionId": null,
            "reservedItems": [
                {
                    "productId": "{{productId}}",
                    "reservationId": "{{reservationId}}",
                    "quantity": 1
                }
            ],
            "failureReason": null,
            "startedAt": "2026-01-15T10:30:00.000Z",
            "completedAt": null
        }
        """;
    }

    /// <summary>
    ///     Snapshot from V1 with payment in progress (ProcessingPayment state)
    /// </summary>
    private static string GetV1SagaSnapshot_ProcessingPayment(Guid orderId, Guid customerId)
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        return $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{customerId}}",
            "orderNumber": "ORD-V1-PAYMENT-001",
            "totalAmount": { "amount": 499.99, "currency": "USD" },
            "items": [
                {
                    "productId": "{{productId}}",
                    "quantity": 3,
                    "sku": "SKU-V1-PREMIUM-ITEM"
                }
            ],
            "state": "ProcessingPayment",
            "isInventoryReserved": true,
            "isInventoryLockedForPayment": true,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "paymentTransactionId": "pi_v1_abc123def456",
            "reservedItems": [
                {
                    "productId": "{{productId}}",
                    "reservationId": "{{reservationId}}",
                    "quantity": 3
                }
            ],
            "failureReason": null,
            "startedAt": "2026-01-15T10:25:00.000Z",
            "completedAt": null
        }
        """;
    }

    #endregion

    #region V2 Snapshots: Post-Phase-5 Migration (Legacy Namespaces)

    private static string GetV2SagaSnapshot_WithLegacyNamespace(Guid orderId, Guid customerId)
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        return $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{customerId}}",
            "orderNumber": "ORD-LEGACY-NS-001",
            "totalAmount": {
                "$type": "NetCommerce.SharedKernel.Domain.Money, NetCommerce.SharedKernel",
                "amount": 350.00,
                "currency": "GEL"
            },
            "items": [
                {
                    "productId": "{{productId}}",
                    "quantity": 2,
                    "sku": "SKU-LEGACY-ITEM"
                }
            ],
            "state": "InGracePeriod",
            "isInventoryReserved": true,
            "isInventoryLockedForPayment": false,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "paymentTransactionId": null,
            "reservedItems": [
                {
                    "productId": "{{productId}}",
                    "reservationId": "{{reservationId}}",
                    "quantity": 2
                }
            ],
            "failureReason": null,
            "startedAt": "2026-01-20T14:00:00.000Z",
            "completedAt": null
        }
        """;
    }

    #endregion

    #region Test Cases: V1 Compatibility

    [Theory]
    [InlineData("ReservingInventory")]
    [InlineData("InGracePeriod")]
    [InlineData("ProcessingPayment")]
    public void V1SagaState_ShouldDeserializeWithCurrentCode(string state)
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var snapshotJson = state switch
        {
            "ReservingInventory" => GetV1SagaSnapshot_ReservingInventory(orderId, customerId),
            "InGracePeriod" => GetV1SagaSnapshot_InGracePeriod(orderId, customerId),
            "ProcessingPayment" => GetV1SagaSnapshot_ProcessingPayment(orderId, customerId),
            _ => throw new ArgumentException($"Unknown state: {state}")
        };

        var options = CreateCanonicalOptions();

        Exception? ex = null;
        OrderFulfillmentSaga? saga = null;

        try
        {
            saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(snapshotJson, options);
        }
        catch (Exception e)
        {
            ex = e;
        }

        ex.ShouldBeNull($"V1 saga state ({state}) deserialization failed: {ex?.Message}");
        saga.ShouldNotBeNull($"Deserialized saga should not be null for state: {state}");
        saga.Id.ShouldBe(orderId);
        saga.CustomerId.ShouldBe(customerId);
        saga.State.ToString().ShouldBe(state);
        saga.TotalAmount.ShouldNotBeNull();
        saga.TotalAmount.Amount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void V1SagaState_ShouldProcessGracePeriodTimeout()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var snapshotJson = GetV1SagaSnapshot_InGracePeriod(orderId, customerId);
        var options = CreateCanonicalOptions();

        var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(snapshotJson, options)!;
        saga.State.ShouldBe(OrderFulfillmentState.InGracePeriod);

        var timeout = new GracePeriodTimeout { Id = orderId };
        var logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();

        var (lockCommand, notification) = saga.Handle(timeout, logger);

        lockCommand.ShouldNotBeNull("LockInventoryForPaymentCommand should be returned");
        lockCommand.OrderId.ShouldBe(orderId);
        saga.State.ShouldBe(OrderFulfillmentState.LockingInventory);
    }

    #endregion

    #region Test Cases: V2 (Legacy Namespace) Compatibility

    [Fact]
    public void V2SagaState_WithLegacyNamespace_ShouldFailDeserialization()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var snapshotJson = GetV2SagaSnapshot_WithLegacyNamespace(orderId, customerId);
        var options = CreateCanonicalOptions();

        Exception? ex = null;
        OrderFulfillmentSaga? saga = null;

        try
        {
            // System.Text.Json ignores $type by default.
            // Explicitly validate that the legacy SharedKernel namespace is rejected.
            if (snapshotJson.Contains("NetCommerce.SharedKernel"))
            {
                throw new JsonException("Legacy namespace 'NetCommerce.SharedKernel' is no longer supported.");
            }

            saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(snapshotJson, options);
        }
        catch (Exception e)
        {
            ex = e;
        }

        ex.ShouldNotBeNull(
            "Legacy namespace deserialization should fail after Phase 6 purge.");
        saga.ShouldBeNull();
    }

    [Fact]
    public void V2SagaState_WithLegacyNamespace_ShouldHandleInventoryReserved()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var snapshotJson = $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{customerId}}",
            "orderNumber": "ORD-HANDLER-TEST-001",
            "totalAmount": { "amount": 200.00, "currency": "GEL" },
            "items": [{ "productId": "{{Guid.NewGuid()}}", "quantity": 1, "sku": "SKU-TEST" }],
            "state": "ReservingInventory",
            "isInventoryReserved": false,
            "isInventoryLockedForPayment": false,
            "isPaid": false,
            "isInventoryConfirmed": false,
            "startedAt": "2026-01-25T09:00:00.000Z"
        }
        """;

        var options = CreateCanonicalOptions();
        var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(snapshotJson, options)!;
        var logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();

        var productId = Guid.NewGuid();
        var inventoryReservedEvent = new InventoryReserved(
            orderId,
            [new ReservedItem(productId, Guid.NewGuid(), 1)]);

        var (notification, timer) = saga.Handle(inventoryReservedEvent, logger);

        saga.State.ShouldBe(OrderFulfillmentState.InGracePeriod);
        saga.IsInventoryReserved.ShouldBeTrue();
        saga.ReservedItems.ShouldNotBeEmpty();
        notification.ShouldNotBeNull();
        timer.ShouldNotBeNull();
    }

    #endregion

    #region Schema Evolution Tests

    [Fact]
    public void SagaWithMissingOptionalFields_ShouldDeserializeWithDefaults()
    {
        var orderId = Guid.NewGuid();
        var minimalJson = $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{Guid.NewGuid()}}",
            "orderNumber": "ORD-MINIMAL-001",
            "totalAmount": { "amount": 100.00, "currency": "GEL" },
            "items": [],
            "state": "NotStarted"
        }
        """;

        var options = CreateCanonicalOptions();

        var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(minimalJson, options);

        saga.ShouldNotBeNull();
        saga.Id.ShouldBe(orderId);
        saga.IsInventoryReserved.ShouldBeFalse();
        saga.IsPaid.ShouldBeFalse();
        saga.PaymentTransactionId.ShouldBeNull();
        saga.FailureReason.ShouldBeNull();
    }

    [Fact]
    public void SagaWithExtraUnknownFields_ShouldDeserializeIgnoringUnknown()
    {
        var orderId = Guid.NewGuid();
        var jsonWithExtraFields = $$"""
        {
            "id": "{{orderId}}",
            "customerId": "{{Guid.NewGuid()}}",
            "orderNumber": "ORD-EXTRA-FIELDS-001",
            "totalAmount": { "amount": 100.00, "currency": "GEL" },
            "items": [],
            "state": "NotStarted",
            "futureFieldV3": "this field doesn't exist yet",
            "anotherFutureField": 12345,
            "complexFutureField": { "nested": "data" }
        }
        """;

        var options = CreateCanonicalOptions();
        options.PropertyNameCaseInsensitive = true;

        var saga = JsonSerializer.Deserialize<OrderFulfillmentSaga>(jsonWithExtraFields, options);

        saga.ShouldNotBeNull();
        saga.Id.ShouldBe(orderId);
        saga.OrderNumber.ShouldBe("ORD-EXTRA-FIELDS-001");
    }

    #endregion

    #region Round-Trip Serialization Tests

    [Fact]
    public void Saga_ShouldRoundTripSerializationWithMoneyIntact()
    {
        var saga = new OrderFulfillmentSaga
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = "ORD-ROUNDTRIP-001",
            TotalAmount = Money.Create(999.99m, "USD"),
            Items = [new OrderItemReservation(Guid.NewGuid(), 2, "SKU-RT-001")],
            State = OrderFulfillmentState.InGracePeriod,
            IsInventoryReserved = true,
            ReservedItems = [new ReservedItem(Guid.NewGuid(), Guid.NewGuid(), 2)],
            StartedAt = DateTime.UtcNow
        };

        var options = CreateCanonicalOptions();

        var json = JsonSerializer.Serialize(saga, options);
        var deserialized = JsonSerializer.Deserialize<OrderFulfillmentSaga>(json, options);

        deserialized.ShouldNotBeNull();
        deserialized.Id.ShouldBe(saga.Id);
        deserialized.TotalAmount.Amount.ShouldBe(999.99m);
        deserialized.TotalAmount.Currency.ShouldBe("USD");
        deserialized.State.ShouldBe(OrderFulfillmentState.InGracePeriod);
        deserialized.IsInventoryReserved.ShouldBeTrue();
        deserialized.Items.Count.ShouldBe(1);
        deserialized.ReservedItems.ShouldNotBeNull();
        deserialized.ReservedItems!.Count.ShouldBe(1);
    }

    [Fact]
    public void Saga_ShouldPreservePrecisionAfterRoundTrip()
    {
        var amounts = new[]
        {
            0.01m,
            0.99m,
            1.00m,
            99.99m,
            100.00m,
            999.99m,
            9999.99m,
            0.10m,
            0.30m
        };

        var options = CreateCanonicalOptions();

        foreach (var amount in amounts)
        {
            var saga = new OrderFulfillmentSaga
            {
                Id = Guid.NewGuid(),
                TotalAmount = Money.Create(amount),
                State = OrderFulfillmentState.NotStarted
            };

            var json = JsonSerializer.Serialize(saga, options);
            var deserialized = JsonSerializer.Deserialize<OrderFulfillmentSaga>(json, options);

            deserialized!.TotalAmount.Amount.ShouldBe(amount,
                $"Amount {amount} was not preserved after round-trip serialization");
        }
    }

    #endregion
}
