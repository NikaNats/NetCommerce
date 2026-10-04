#nullable enable
using NetCommerce.Ordering.Application.Orders.Queries;

namespace NetCommerce.Ordering.Application.Orders.Services;

/// <summary>
///     PEAA Gateway over Wolverine's saga store.
/// </summary>
/// <remarks>
/// <para>
///     The saga table is owned by Wolverine's message store, not by any module
///     DbContext model — it is an external resource from the domain's point of
///     view. This gateway centralizes that record access so endpoints and
///     handlers never embed raw SQL or connection handling.
/// </para>
/// </remarks>
public interface ISagaStateGateway
{
    Task<IReadOnlyList<StuckSagaInfoDto>> GetStuckSagasAsync(
        CancellationToken cancellationToken = default);
}
