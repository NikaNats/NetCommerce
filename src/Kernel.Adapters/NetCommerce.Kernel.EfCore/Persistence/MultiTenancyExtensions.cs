#nullable enable
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.EfCore.Persistence;

public static class MultiTenancyExtensions
{
    private static readonly MethodInfo EfPropertyMethod = typeof(EF)
        .GetMethod(nameof(EF.Property), BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(object), typeof(string)],
            modifiers: null)!
        .MakeGenericMethod(typeof(string));

    /// <summary>
    ///     Automatically applies Query Filters for ISoftDelete and IMultiTenant.
    ///
    ///     Composition rule: an entity implementing BOTH interfaces gets a SINGLE
    ///     combined filter (<c>not-deleted AND same-tenant</c>). EF Core keeps only
    ///     the last <c>HasQueryFilter</c> call per entity, so applying them
    ///     separately would silently drop one predicate and leak rows.
    /// </summary>
    public static void ApplyKernelGlobalFilters(this ModelBuilder modelBuilder, BaseDbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            var isSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clrType);
            var isMultiTenant = typeof(IMultiTenant).IsAssignableFrom(clrType);

            if (!isSoftDelete && !isMultiTenant)
                continue;

            var parameter = Expression.Parameter(clrType, "e");
            Expression? body = null;

            if (isSoftDelete)
            {
                var deletedAt = Expression.Property(parameter, nameof(ISoftDelete.DeletedAt));
                body = Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTime?)));
            }

            if (isMultiTenant)
            {
                // EF.Property<string>(e, "TenantId") == context.CurrentTenantId.
                // The closure-captured CurrentTenantId is parameterized by EF Core,
                // so the filter value is evaluated fresh on each query execution.
                var tenantId = Expression.Call(
                    EfPropertyMethod,
                    parameter,
                    Expression.Constant(nameof(IMultiTenant.TenantId)));
                var currentTenant = Expression.Property(
                    Expression.Constant(context),
                    nameof(BaseDbContext.CurrentTenantId));
                var sameTenant = Expression.Equal(tenantId, currentTenant);

                body = body is null ? sameTenant : Expression.AndAlso(body, sameTenant);
            }

            modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(body!, parameter));
        }
    }
}
