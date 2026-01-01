using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Events;

/// <summary>
/// Integration events for cross-module communication.
/// These are mirrors of domain events published in different modules.
/// They allow modules to communicate without direct coupling.
/// </summary>
/// 
public sealed record OrderPaidIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Money TotalAmount) : IntegrationEvent;

public sealed record OrderCreatedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId) : IntegrationEvent;

public sealed record PaymentCompletedIntegrationEvent(
    Guid TransactionId,
    Guid OrderId,
    Money Amount) : IntegrationEvent;

/// <summary>
/// Raised when inventory confirmation cannot be completed after a successful payment
/// and the originating outbox message has exhausted retries.
/// Used to trigger compensating actions (e.g., refund) or support alerting.
/// </summary>
public sealed record OrderInventoryConfirmationFailedIntegrationEvent(
    Guid OrderId,
    Guid PaymentTransactionId,
    Money Amount,
    string FailureReason,
    string? FailureDetails) : IntegrationEvent;

public sealed record StockReservedIntegrationEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int RemainingAvailable) : IntegrationEvent;

public sealed record StockDeductedIntegrationEvent(
    Guid StockId,
    Guid ProductId,
    Guid OrderId,
    int Quantity,
    int NewTotal) : IntegrationEvent;
