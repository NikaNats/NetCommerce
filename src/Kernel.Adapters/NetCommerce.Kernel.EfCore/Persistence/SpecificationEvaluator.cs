#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Evaluates specifications against EF Core queryables.
/// </summary>
public static class SpecificationEvaluator
{
    public static IQueryable<T> GetQuery<T>(
        IQueryable<T> query,
        ISpecification<T> specification,
        bool evaluateCriteriaOnly = false) where T : class
    {
        // Apply filtering
        if (specification.Criteria is not null)
        {
            query = query.Where(specification.Criteria);
        }

        if (evaluateCriteriaOnly)
        {
            return query;
        }

        // Apply includes
        query = specification.Includes
            .Aggregate(query, (current, include) => current.Include(include));

        query = specification.IncludeStrings
            .Aggregate(query, (current, include) => current.Include(include));

        // Apply ordering
        if (specification.OrderBy is not null)
        {
            query = query.OrderBy(specification.OrderBy);
        }
        else if (specification.OrderByDescending is not null)
        {
            query = query.OrderByDescending(specification.OrderByDescending);
        }

        // Apply paging
        if (specification.Skip.HasValue)
        {
            query = query.Skip(specification.Skip.Value);
        }

        if (specification.Take.HasValue)
        {
            query = query.Take(specification.Take.Value);
        }

        // Apply no tracking
        if (specification.AsNoTracking)
        {
            query = query.AsNoTracking();
        }

        return query;
    }
}
