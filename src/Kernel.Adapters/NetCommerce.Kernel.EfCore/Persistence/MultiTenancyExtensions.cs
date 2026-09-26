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
                //
                // NULL-TOLERANT: system scopes without an ambient HTTP request
                // (Wolverine message handlers, IHostedService workers) resolve no
                // tenant. Without the bypass, their filter would evaluate
                // tenant_id = NULL and silently return zero rows — e.g.
                // GracePeriodManagerService would never find orders and sagas
                // would never start. When a tenant IS ambient (HTTP requests
                // carrying the tenant claim/header), isolation stays strict.
                // NOTE: tenantless HTTP traffic is treated as system traffic;
                // per-customer isolation for those routes still comes from the
                // JWT subject scoping in the endpoints/handlers themselves.
                var tenantId = Expression.Call(
                    EfPropertyMethod,
                    parameter,
                    Expression.Constant(nameof(IMultiTenant.TenantId)));
                var currentTenant = Expression.Property(
                    Expression.Constant(context),
                    nameof(BaseDbContext.CurrentTenantId));
                var noAmbientTenant = Expression.Equal(
                    currentTenant,
                    Expression.Constant(null, currentTenant.Type));
                var sameTenant = Expression.Equal(tenantId, currentTenant);
                var tenantFilter = Expression.OrElse(noAmbientTenant, sameTenant);

                body = body is null ? tenantFilter : Expression.AndAlso(body, tenantFilter);
            }

            modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(body!, parameter));
        }
    }
}
