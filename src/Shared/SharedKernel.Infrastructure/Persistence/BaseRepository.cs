using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Base repository implementation using Entity Framework Core.
/// </summary>
public abstract class BaseRepository<TAggregate, TId> : IRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    protected readonly DbContext Context;
    protected readonly DbSet<TAggregate> DbSet;

    protected BaseRepository(DbContext context)
    {
        Context = context;
        DbSet = context.Set<TAggregate>();
    }

    public virtual async Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<IReadOnlyList<TAggregate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(aggregate, cancellationToken);
    }

    public virtual void Update(TAggregate aggregate)
    {
        DbSet.Update(aggregate);
    }

    public virtual void Remove(TAggregate aggregate)
    {
        DbSet.Remove(aggregate);
    }
}
