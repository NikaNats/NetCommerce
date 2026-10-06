using NetCommerce.Domain.Shared;

namespace NetCommerce.Domain.Tests.SharedKernel;

/// <summary>
///     The dedup contract is byte-for-byte agreement between producers.
///     These pin the key families so a prefix rename or second producer
///     cannot silently fork it.
/// </summary>
public class IdempotencyKeysTests
{
    [Fact]
    public void ForShadowOrder_IsStablePerGhostCharge()
    {
        IdempotencyKeys.ForShadowOrder("txn_1").ShouldBe("shadow-txn_1");
        IdempotencyKeys.ForShadowOrder("txn_1").ShouldBe(IdempotencyKeys.ForShadowOrder("txn_1"));
    }

    [Fact]
    public void ForShadowOrder_DiffersFromReconciliationRefundKey()
    {
        IdempotencyKeys.ForShadowOrder("txn_1")
            .ShouldNotBe(IdempotencyKeys.ForReconciliationRefund("txn_1"));
    }

    [Fact]
    public void ForShadowOrder_TrimsInput()
    {
        IdempotencyKeys.ForShadowOrder("  txn_1  ").ShouldBe("shadow-txn_1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShadowAndRefundKeys_RejectEmpty(string? txnId)
    {
        Should.Throw<ArgumentException>(() => IdempotencyKeys.ForShadowOrder(txnId!));
        Should.Throw<ArgumentException>(() => IdempotencyKeys.ForReconciliationRefund(txnId!));
    }

    [Fact]
    public void ForOrderRefund_IsStablePerOrder()
    {
        var orderId = Guid.NewGuid();
        IdempotencyKeys.ForOrderRefund(orderId).ShouldBe($"refund_{orderId}");
    }
}
