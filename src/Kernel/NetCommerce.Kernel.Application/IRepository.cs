#nullable enable
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.Application;

/// <summary>
///     Generic repository interface for aggregate roots.
/// </summary>
public interface IRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);
    void Update(TAggregate aggregate);
    void Remove(TAggregate aggregate);
}

/// <summary>
///     Read-only repository interface for query operations.
/// </summary>
public interface IReadOnlyRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
}
