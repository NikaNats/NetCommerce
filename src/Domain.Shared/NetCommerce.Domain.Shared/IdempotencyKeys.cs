#nullable enable
namespace NetCommerce.Domain.Shared;

/// <summary>
///     Single owner for idempotency/dedup key formats (pragmatic DRY).
/// </summary>
/// <remarks>
/// <para>
///     PSPs and stores deduplicate by EXACT key match, so the format of each key
///     family is system knowledge: producers (saga, reconciliation, checkout)
///     and any future verifier must agree byte-for-byte. These lived as string
///     interpolations scattered across modules — a prefix rename or a second
///     producer would silently fork the dedup contract and double-charge.
///     Derive every key from here.
/// </para>
/// <para>
///     Keying rule: each key identifies one BUSINESS INTENT, stable across
///     retries and redeliveries (never per attempt). Two different intents for
///     one order (admin action vs saga compensation) MUST use different keys.
/// </para>
/// </remarks>
public static class IdempotencyKeys
{
    /// <summary>One payment initiation per Wolverine envelope delivery.</summary>
    public static string ForPayment(Guid envelopeId) => $"payment_{envelopeId}";

    /// <summary>One saga compensation refund per order.</summary>
    public static string ForOrderRefund(Guid orderId) => $"refund_{orderId}";

    /// <summary>One shadow order per ghost charge.</summary>
    public static string ForShadowOrder(string externalTransactionId)
    {
        if (string.IsNullOrWhiteSpace(externalTransactionId))
            throw new ArgumentException("External transaction id is required.", nameof(externalTransactionId));
        return $"shadow-{externalTransactionId.Trim()}";
    }

    /// <summary>One reconciliation refund per ghost charge.</summary>
    public static string ForReconciliationRefund(string externalTransactionId)
    {
        if (string.IsNullOrWhiteSpace(externalTransactionId))
            throw new ArgumentException("External transaction id is required.", nameof(externalTransactionId));
        return $"reconcile-refund_{externalTransactionId.Trim()}";
    }
}
