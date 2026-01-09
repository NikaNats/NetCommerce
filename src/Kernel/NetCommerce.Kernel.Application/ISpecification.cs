#nullable enable
using System.Linq.Expressions;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.Application;

/// <summary>
///     Specification pattern interface for encapsulating query logic.
///     Enables reusable, composable, and testable query criteria.
/// </summary>
public interface ISpecification<T> where T : class
{
    /// <summary>
    ///     The filter expression for this specification.
    /// </summary>
    Expression<Func<T, bool>>? Criteria { get; }

    /// <summary>
    ///     Includes for eager loading navigation properties.
    /// </summary>
    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }

    /// <summary>
    ///     String-based includes for nested navigation properties.
    /// </summary>
    IReadOnlyList<string> IncludeStrings { get; }

    /// <summary>
    ///     Order by expression (ascending).
    /// </summary>
    Expression<Func<T, object>>? OrderBy { get; }

    /// <summary>
    ///     Order by expression (descending).
    /// </summary>
    Expression<Func<T, object>>? OrderByDescending { get; }

    /// <summary>
    ///     Number of records to skip.
    /// </summary>
    int? Skip { get; }

    /// <summary>
    ///     Number of records to take.
    /// </summary>
    int? Take { get; }

    /// <summary>
    ///     Whether to disable tracking for query results.
    /// </summary>
    bool AsNoTracking { get; }
}

/// <summary>
///     Base implementation of the specification pattern.
/// </summary>
public abstract class Specification<T> : ISpecification<T> where T : class
{
    private readonly List<Expression<Func<T, object>>> _includes = [];
    private readonly List<string> _includeStrings = [];

    public Expression<Func<T, bool>>? Criteria { get; protected set; }
    public IReadOnlyList<Expression<Func<T, object>>> Includes => _includes.AsReadOnly();
    public IReadOnlyList<string> IncludeStrings => _includeStrings.AsReadOnly();
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    public int? Skip { get; private set; }
    public int? Take { get; private set; }
    public bool AsNoTracking { get; private set; }

    protected void AddInclude(Expression<Func<T, object>> includeExpression)
    {
        _includes.Add(includeExpression);
    }

    protected void AddInclude(string includeString)
    {
        _includeStrings.Add(includeString);
    }

    protected void ApplyOrderBy(Expression<Func<T, object>> orderByExpression)
    {
        OrderBy = orderByExpression;
    }

    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescendingExpression)
    {
        OrderByDescending = orderByDescendingExpression;
    }

    protected void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
    }

    protected void ApplyNoTracking()
    {
        AsNoTracking = true;
    }
}

/// <summary>
///     Repository interface with specification support.
/// </summary>
public interface ISpecificationRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    Task<TAggregate?> GetBySpecAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);
    Task<int> CountAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);
}
