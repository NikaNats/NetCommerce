#nullable enable
using NetCommerce.Ordering.Application.Orders.Queries;
using NetCommerce.Ordering.Application.Orders.Services;
using NetCommerce.Ordering.Application.Sagas;

namespace NetCommerce.Ordering.Infrastructure.Persistence;

/// <summary>
///     Gateway implementation over Wolverine's saga store table.
///     Centralizes the raw-SQL read path (see <see cref="WolverineSagaStateReader"/>)
///     so no endpoint or workflow handler manages connections or SQL.
/// </summary>
public sealed class WolverineSagaStateGateway(OrderingDbContext db) : ISagaStateGateway
{
    public async Task<IReadOnlyList<StuckSagaInfoDto>> GetStuckSagasAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await WolverineSagaStateReader.QuerySagasAsync(
            db,
            OrderFulfillmentState.ManualInterventionRequired,
            cancellationToken: cancellationToken);

        return rows
            .OrderBy(s => s.StartedAt)
            .Select(s => new StuckSagaInfoDto(
                s.OrderId,
                s.OrderNumber,
                s.PaymentTransactionId ?? "N/A",
                s.FailureReason ?? "Unknown reason",
                s.StartedAt,
                s.TotalAmount.Amount,
                s.TotalAmount.Currency))
            .ToList();
    }
}
