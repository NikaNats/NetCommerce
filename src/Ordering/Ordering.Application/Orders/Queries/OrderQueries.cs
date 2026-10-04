#nullable enable
using NetCommerce.Kernel.Application;
using NetCommerce.Ordering.Domain.Orders;

namespace NetCommerce.Ordering.Application.Orders.Queries;

/// <summary>
///     Read-side Service Layer for orders (PEAA Service Layer + Remote Facade).
/// </summary>
/// <remarks>
/// <para>
///     Presentation must not reach into persistence. These queries define the
///     application-oriented read API; the Infrastructure handlers own repository
///     access, ownership enforcement, and DTO translation. Endpoints stay thin:
///     extract the caller identity from transport, dispatch via the bus, map to
///     the wire contract.
/// </para>
/// </remarks>

/// <summary>
///     Authoritative persisted state of one order for storefront reconciliation.
///     Deliberately minimal: no addresses, no payment identifiers, no items —
///     the order page needs status and timeline only.
/// </summary>
public sealed record OrderDetailsDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime? PaidAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime? CancelledAt,
    string? CancellationReason);

/// <summary>
///     Get one order, enforcing that the requestor owns it.
///     Failures: NotFound when absent, Forbidden on owner mismatch (fail closed).
/// </summary>
public record GetOrderByIdQuery(
    Guid OrderId,
    Guid RequestorCustomerId) : IQuery<OrderDetailsDto>;

/// <summary>
///     Resolve the owning customer of an order (for pre-dispatch authorization).
/// </summary>
public record GetOrderOwnerQuery(
    Guid OrderId) : IQuery<Guid>;

/// <summary>
///     One saga parked in manual intervention, translated to application terms.
///     Amount is decomposed so Application does not force a Money dependency
///     on every consumer; Infrastructure maps from the saga store row.
/// </summary>
public sealed record StuckSagaInfoDto(
    Guid OrderId,
    string OrderNumber,
    string PaymentTransactionId,
    string RefundFailureReason,
    DateTime StuckSince,
    decimal Amount,
    string Currency);

/// <summary>
///     List sagas requiring manual intervention (admin observability read).
/// </summary>
public record GetStuckSagasQuery : IQuery<IReadOnlyList<StuckSagaInfoDto>>;
