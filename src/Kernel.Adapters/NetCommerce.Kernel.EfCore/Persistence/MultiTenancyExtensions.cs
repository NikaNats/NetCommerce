#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.EfCore.Persistence;

public static class MultiTenancyExtensions
{
    // MethodInfo cache for performance optimization
    private static readonly MethodInfo ConfigureTenantFilterMethod = typeof(MultiTenancyExtensions)
        .GetMethod(nameof(ConfigureTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find ConfigureTenantFilter method.");

    /// <summary>
    ///     Automatically applies Query Filters for ISoftDelete and IMultiTenant.
    /// </summary>
    [RequiresUnreferencedCode("Multi-tenancy filter uses reflection to apply query filters dynamically.")]
    [RequiresDynamicCode("Multi-tenancy filter uses MakeGenericMethod and expression trees.")]
    public static void ApplyKernelGlobalFilters(this ModelBuilder modelBuilder, BaseDbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // 1. Configure Soft Delete
            if (typeof(ISoftDelete).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasQueryFilter(GetSoftDeleteFilter(clrType));
            }

            // 2. Configure Multi-Tenancy
            if (typeof(IMultiTenant).IsAssignableFrom(clrType))
            {
                // We must use reflection to call the generic method because
                // HasQueryFilter<T> requires the generic type argument.
                var genericMethod = ConfigureTenantFilterMethod.MakeGenericMethod(clrType);
                genericMethod.Invoke(null, [modelBuilder]);
            }
        }
    }

    /// <summary>
    ///     This method is called via Reflection for each IMultiTenant entity.
    /// </summary>
    private static void ConfigureTenantFilter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(ModelBuilder builder)
        where TEntity : class, IMultiTenant
    {
        builder.Entity<TEntity>().HasQueryFilter(e =>
            // EF Core captures 'EF.Property' to allow dynamic resolution
            EF.Property<string>(e, nameof(IMultiTenant.TenantId)) ==
            // It accesses the property on the DbContext instance
            EF.Property<string?>(EF.Property<BaseDbContext>(e, "Context"), nameof(BaseDbContext.CurrentTenantId))
        );

        // OPTIONAL: Add Index for performance
        builder.Entity<TEntity>().HasIndex(e => e.TenantId);
    }

    private static LambdaExpression GetSoftDeleteFilter(Type type)
    {
        var parameter = Expression.Parameter(type, "e");
        var property = Expression.Property(parameter, nameof(ISoftDelete.DeletedAt));
        var filter = Expression.Equal(property, Expression.Constant(null));
        return Expression.Lambda(filter, parameter);
    }
}
