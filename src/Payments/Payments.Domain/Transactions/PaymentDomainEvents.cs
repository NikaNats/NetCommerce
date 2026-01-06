using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Payments.Domain.Transactions;

// Domain Events for Payment

public sealed record PaymentCompletedDomainEvent(
    string ExternalTransactionId,
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
