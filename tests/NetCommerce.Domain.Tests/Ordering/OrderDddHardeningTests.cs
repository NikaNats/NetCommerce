using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Tests.Fakers;
using NetCommerce.Ordering.Domain.Orders;

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Executable examples for the DDD hardening pass:
///     weight validation, currency consistency, shadow-order guards,
///     cancellation policy, and idempotent grace-period confirmation.
/// </summary>
public class OrderDddHardeningTests
{
    private static Order CreateOrderWithGelItem(decimal weightKg = 1.5m)
    {
        var order = Order.Create(Guid.NewGuid(), ShippingAddressFaker.Generate(), Guid.NewGuid().ToString());
        var price = Money.Create(100m, "GEL");
        order.AddItem(Guid.NewGuid(), "Test Product", price, 1, weightKg, PriceBreakdown.CreateSimple(100m, "GEL"));
        return order;
    }

    [Fact]
    public void AddItem_WithNegativeWeight_ShouldThrow()
    {
        var order = OrderFaker.Generate();
        var price = Money.Create(10m, "GEL");

        Should.Throw<ArgumentOutOfRangeException>(() =>
            order.AddItem(Guid.NewGuid(), "P", price, 1, -2m, PriceBreakdown.CreateSimple(10m, "GEL")));
    }

    [Fact]
    public void AddItem_WithMixedCurrencies_ShouldThrowWithClearMessage()
    {
        var order = CreateOrderWithGelItem();
        var usd = Money.Create(10m, "USD");

        var ex = Should.Throw<InvalidOperationException>(() =>
            order.AddItem(Guid.NewGuid(), "P", usd, 1, 1m, PriceBreakdown.CreateSimple(10m, "USD")));

        ex.Message.ShouldContain("one currency");
    }

    [Fact]
    public void AddItem_OnShadowOrder_ShouldThrow()
    {
        var shadow = Order.CreateShadowOrder(
            "txn_123",
            Money.Create(50m, "GEL"),
            ShippingAddressFaker.Generate(),
            "admin",
            "ghost charge");

        Should.Throw<InvalidOperationException>(() =>
            shadow.AddItem(Guid.NewGuid(), "P", Money.Create(10m, "GEL"), 1, 1m, PriceBreakdown.CreateSimple(10m, "GEL")));
    }

    [Fact]
    public void Create_WithEmptyCustomer_ShouldThrow()
    {
        Should.Throw<ArgumentException>(() =>
            Order.Create(Guid.Empty, ShippingAddressFaker.Generate(), Guid.NewGuid().ToString()));
    }

    [Fact]
    public void CreateShadowOrder_WithoutAuditNotes_ShouldThrow()
    {
        Should.Throw<ArgumentException>(() =>
            Order.CreateShadowOrder("txn_1", Money.Create(10m, "GEL"), ShippingAddressFaker.Generate(), "admin", ""));
    }

    [Theory]
    [InlineData(OrderStatus.Submitted, true)]
    [InlineData(OrderStatus.AwaitingValidation, true)]
    [InlineData(OrderStatus.Paid, true)]
    [InlineData(OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void CancellationPolicy_CanCancel_MatchesExpected(OrderStatus status, bool expected)
    {
        OrderCancellationPolicy.CanCancel(status).ShouldBe(expected);
    }

    [Fact]
    public void Cancel_WithoutReason_ShouldThrow()
    {
        var order = OrderFaker.Generate();
        Should.Throw<ArgumentException>(() => order.Cancel("  "));
    }

    [Fact]
    public void Cancel_TrimsAndStoresReason()
    {
        var order = OrderFaker.Generate();
        order.Cancel("  Changed my mind  ");
        order.CancellationReason.ShouldBe("Changed my mind");
    }

    [Fact]
    public void Cancel_WithTooLongReason_ShouldThrow()
    {
        var order = OrderFaker.Generate();
        Should.Throw<ArgumentOutOfRangeException>(() => order.Cancel(new string('x', 501)));
    }

    [Fact]
    public void TryConfirmGracePeriod_Twice_SecondReturnsFalseWithoutEvents()
    {
        var order = OrderFaker.Generate();

        order.TryConfirmGracePeriod().ShouldBeTrue();
        order.ClearDomainEvents();

        order.TryConfirmGracePeriod().ShouldBeFalse();
        order.DomainEvents.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.AwaitingValidation);
    }

    [Fact]
    public void WeightKg_RoundsToThreeDecimals()
    {
        WeightKg.Create(1.23456m).Value.ShouldBe(1.235m);
        WeightKg.Create(0m).Value.ShouldBe(0m);
    }
}
