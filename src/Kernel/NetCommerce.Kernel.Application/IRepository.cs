#nullable enable
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.Application;

/// <summary>
///     Generic repository interface for aggregate roots.
/// </summary>
/// <remarks>
/// A read-write repository IS-A read-only repository: the read members are
/// identical, so this extends <see cref="IReadOnlyRepository{TAggregate, TId}"/>
/// instead of duplicating its shape. This makes narrowing honest — e.g.
/// Catalog's read-only product view over the cached repository is a safe
/// upcast, not a cast that can fail at runtime.
/// </remarks>
public interface IRepository<TAggregate, TId> : IReadOnlyRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    // GetByIdAsync / GetAllAsync are NOT redeclared here. They are inherited from
    // IReadOnlyRepository with identical signatures, and redeclaring them is
    // CS0108 (member hides inherited member) — an ERROR, because pr-validation
    // builds with TreatWarningsAsErrors=true.
    //
    // This only surfaced now because the declaration order was originally the other
    // way round (IReadOnlyRepository extended IRepository), so re-stating the read
    // members was a genuine override rather than a redefinition. Inverting the
    // hierarchy makes them inherited, and the old lines became dead weight.
    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);
    void Update(TAggregate aggregate);
    void Remove(TAggregate aggregate);
}

/// <summary>
///     Read-only repository interface for query operations.
/// </summary>
public interface IReadOnlyRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
}
