#nullable enable
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Base repository implementation using Entity Framework Core.
/// </summary>
public abstract class BaseRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate, TId> : IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
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
        return await DbSet.FindAsync(id, cancellationToken);
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

/// <summary>
///     Base repository with specification support.
/// </summary>
public abstract class SpecificationRepository<TAggregate, TId> : BaseRepository<TAggregate, TId>, ISpecificationRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    protected SpecificationRepository(DbContext context) : base(context)
    {
    }

    public virtual async Task<TAggregate?> GetBySpecAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    public virtual async Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).ToListAsync(cancellationToken);
    }

    public virtual async Task<int> CountAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, evaluateCriteriaOnly: true).CountAsync(cancellationToken);
    }

    public virtual async Task<bool> AnyAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, evaluateCriteriaOnly: true).AnyAsync(cancellationToken);
    }

    protected internal IQueryable<TAggregate> ApplySpecification(ISpecification<TAggregate> specification, bool evaluateCriteriaOnly = false)
    {
        return SpecificationEvaluator.GetQuery(DbSet.AsQueryable(), specification, evaluateCriteriaOnly);
    }
}
