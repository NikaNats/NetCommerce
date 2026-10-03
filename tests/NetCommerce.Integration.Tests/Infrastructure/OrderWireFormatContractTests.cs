using System.Text.Json;

using NetCommerce.Api.Endpoints.Ordering;
using NetCommerce.Api.Serialization;
using NetCommerce.Ordering.Domain.Orders;

using Shouldly;

namespace NetCommerce.Integration.Tests.Infrastructure;

/// <summary>
/// Pins the wire contract for the order-read endpoint the storefront consumes.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint did not exist. <c>useOrderSaga.reconcile()</c> has always called
/// <c>GET /api/bff/orders/{id}</c> to re-read authoritative state on load, on
/// reconnect and every 15 seconds, but the API offered only <c>POST /</c>,
/// <c>GET /manual-intervention</c> and <c>DELETE /{orderId}</c>. Every reconcile
/// therefore 404'd and returned early, which is why REST reconciliation was inert:
/// the page could only show what the socket happened to deliver.
/// </para>
/// <para>
/// Written in the style of <c>WireFormatContractTests</c>: against the SHIPPING
/// DTO and the SHIPPING serializer options, so renaming a property breaks this
/// test rather than silently breaking the storefront. That matters here because
/// <c>ApiJsonContext</c> sets <c>WhenWritingNull</c>, so a null timestamp is OMITTED
/// rather than serialized as <c>null</c> — and the frontend types depend on that
/// distinction.
/// </para>
/// </remarks>
public class OrderWireFormatContractTests
{
    private static readonly JsonSerializerOptions Options =
        new(ApiJsonContext.Default.Options);

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, Options);

    private static IReadOnlyList<string> KeysOf(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToList();

    [Fact]
    public void order_status_is_serialized_as_its_numeric_enum_value()
    {
        // The storefront maps this with mapOrderStatus, which accepts a NUMBER. If
        // this ever serialized as a string, every status would arrive as
        // Unknown(<name>) and the page would show a "status unavailable" banner.
        var json = Serialize(new OrderResponse(
            Guid.NewGuid(),
            "NC-1001",
            OrderStatus.StockConfirmed,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null));

        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status");

        status.ValueKind.ShouldBe(JsonValueKind.Number);
        status.GetInt32().ShouldBe((int)OrderStatus.StockConfirmed);
    }

    [Fact]
    public void unset_timestamps_are_omitted_rather_than_null()
    {
        // WhenWritingNull means an absent timestamp is simply not there. The
        // storefront's types declare these optional, and treating a missing key as
        // `null` rather than `undefined` is exactly the mismatch this file exists to
        // prevent.
        var keys = KeysOf(Serialize(new OrderResponse(
            Guid.NewGuid(),
            "NC-1001",
            OrderStatus.Submitted,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null)));

        keys.ShouldContain("createdAt");
        keys.ShouldNotContain("paidAt");
        keys.ShouldNotContain("shippedAt");
        keys.ShouldNotContain("deliveredAt");
        keys.ShouldNotContain("cancelledAt");
        keys.ShouldNotContain("cancellationReason");
    }

    [Fact]
    public void a_settled_timestamps_are_present_when_set()
    {
        // The inverse: a present value must actually appear, or the order timeline
        // would silently lose its milestones.
        var paidAt = DateTime.UtcNow;
        var keys = KeysOf(Serialize(new OrderResponse(
            Guid.NewGuid(),
            "NC-1001",
            OrderStatus.Paid,
            DateTime.UtcNow,
            paidAt,
            null,
            null,
            null,
            null)));

        keys.ShouldContain("paidAt");
    }

    [Fact]
    public void the_core_fields_are_always_present()
    {
        var keys = KeysOf(Serialize(new OrderResponse(
            Guid.NewGuid(),
            "NC-1001",
            OrderStatus.Delivered,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null)));

        keys.ShouldContain("id");
        keys.ShouldContain("orderNumber");
        keys.ShouldContain("status");
        keys.ShouldContain("createdAt");
    }

    [Fact]
    public void the_projection_does_not_leak_customer_or_payment_identifiers()
    {
        // Deliberate shape decision: no addresses, no payment transaction id, no
        // notes. The order page needs status and a timeline, and every extra field is
        // more customer data exposed to a storefront render. This asserts the
        // projection stays minimal so a future "just add X" is a conscious change.
        var keys = KeysOf(Serialize(new OrderResponse(
            Guid.NewGuid(),
            "NC-1001",
            OrderStatus.Submitted,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null)));

        keys.ShouldNotContain("customerId");
        keys.ShouldNotContain("tenantId");
        keys.ShouldNotContain("paymentTransactionId");
        keys.ShouldNotContain("notes");
        keys.ShouldNotContain("idempotencyKey");
        keys.ShouldNotContain("shippingAddress");
        keys.ShouldNotContain("billingAddress");
    }
}