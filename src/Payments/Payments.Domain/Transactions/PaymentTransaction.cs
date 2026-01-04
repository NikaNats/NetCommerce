using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Payments.Domain.Transactions;

/// <summary>
///     Payment transaction aggregate for internal ledger.
///     Never stores raw credit card data (PCI-DSS compliance).
/// </summary>
public sealed class PaymentTransaction : AggregateRoot<Guid>
{
    private PaymentTransaction()
    {
    }

    public Guid OrderId { get; private set; }
    public Money Amount { get; private set; } = default!;
    public PaymentProvider Provider { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? ExternalTransactionId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string? Metadata { get; private set; }

    public static PaymentTransaction Create(
        Guid orderId,
        Money amount,
        PaymentProvider provider,
        string idempotencyKey)
    {
        return new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = amount,
            Provider = provider,
            Status = PaymentStatus.Pending,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetExternalTransactionId(string externalId)
    {
        ExternalTransactionId = externalId;
    }

    public void MarkAsCompleted(string externalTransactionId)
    {
        ExternalTransactionId = externalTransactionId;
        Status = PaymentStatus.Completed;
        CompletedAt = DateTime.UtcNow;

        RaiseDomainEvent(new PaymentCompletedDomainEvent(externalTransactionId, OrderId, Amount));
    }

    public void MarkAsFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        CompletedAt = DateTime.UtcNow;

        RaiseDomainEvent(new PaymentFailedDomainEvent(Id, OrderId, reason));
    }

    public void MarkAsRefunded(string externalRefundId)
    {
        Status = PaymentStatus.Refunded;
        Metadata = $"RefundId:{externalRefundId}";

        RaiseDomainEvent(new PaymentRefundedDomainEvent(Id, OrderId, Amount));
    }
}

public enum PaymentProvider
{
    Stripe = 0,
    BankOfGeorgia = 1,
    TBC = 2
}

public enum PaymentStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Refunded = 4
}
