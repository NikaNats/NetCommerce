using NetCommerce.Kernel.Application;

namespace NetCommerce.Finance.Domain.Reconciliation;

/// <summary>
///     Repository interface for ReconciliationSession aggregate.
/// </summary>
public interface IReconciliationSessionRepository : IRepository<ReconciliationSession, Guid>
{
    Task<ReconciliationSession?> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReconciliationSession>> GetSessionsInDateRangeAsync(
        DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReconciliationSession>> GetMismatchedSessionsAsync(
        DateTime since, CancellationToken cancellationToken = default);
}
