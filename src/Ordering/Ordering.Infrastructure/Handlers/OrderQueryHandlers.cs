#nullable enable
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Ordering.Application.Orders.Queries;
using NetCommerce.Ordering.Application.Orders.Services;
using NetCommerce.Ordering.Domain.Orders;
using Wolverine.Attributes;

namespace NetCommerce.Ordering.Infrastructure.Handlers;

/// <summary>
///     Service Layer read handlers: repository access + ownership + DTO translation.
///     Presentation dispatches these via the bus and never touches persistence.
/// </summary>
[WolverineHandler]
public static class GetOrderByIdHandler
{
    public static async Task<Result<OrderDetailsDto>> HandleAsync(
        GetOrderByIdQuery query,
        IOrderRepository orders,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(query.OrderId, cancellationToken);

        if (order is null)
            return Result.Failure<OrderDetailsDto>(
                Error.NotFound("Order", query.OrderId));

        // Fail closed: owner mismatch is forbidden, never "not found".
        if (order.CustomerId != query.RequestorCustomerId)
            return Result.Failure<OrderDetailsDto>(
                Error.Forbidden("Only the owning customer may read this order."));

        return new OrderDetailsDto(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.CreatedAt,
            order.PaidAt,
            order.ShippedAt,
            order.DeliveredAt,
            order.CancelledAt,
            order.CancellationReason);
    }
}

[WolverineHandler]
public static class GetOrderOwnerHandler
{
    public static async Task<Result<Guid>> HandleAsync(
        GetOrderOwnerQuery query,
        IOrderRepository orders,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(query.OrderId, cancellationToken);

        if (order is null)
            return Result.Failure<Guid>(Error.NotFound("Order", query.OrderId));

        return order.CustomerId;
    }
}

[WolverineHandler]
public static class GetStuckSagasHandler
{
    public static async Task<Result<IReadOnlyList<StuckSagaInfoDto>>> HandleAsync(
        GetStuckSagasQuery _,
        ISagaStateGateway sagas,
        CancellationToken cancellationToken)
    {
        var rows = await sagas.GetStuckSagasAsync(cancellationToken);
        return Result.Success(rows);
    }
}
