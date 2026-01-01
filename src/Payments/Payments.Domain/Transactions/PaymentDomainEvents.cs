using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Payments.Domain.Transactions;

// Domain Events for Payment

public sealed record PaymentCompletedDomainEvent(
    Guid TransactionId,
    Guid OrderId,
    Money Amount) : DomainEvent;

public sealed record PaymentFailedDomainEvent(
    Guid TransactionId,
    Guid OrderId,
    string Reason) : DomainEvent;

public sealed record PaymentRefundedDomainEvent(
    Guid TransactionId,
    Guid OrderId,
    Money Amount) : DomainEvent;