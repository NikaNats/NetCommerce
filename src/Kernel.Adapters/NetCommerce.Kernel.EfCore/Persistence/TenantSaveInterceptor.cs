#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Interceptor to automatically set TenantId on INSERT.
///     Prevents developers from forgetting to assign the tenant.
/// </summary>
public sealed class TenantSaveInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    public TenantSaveInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateTenantEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateTenantEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void UpdateTenantEntities(DbContext? context)
    {
        if (context is null || !_tenantContext.HasTenant) return;

        var tenantId = _tenantContext.TenantId;
        var entries = context.ChangeTracker.Entries<IMultiTenant>();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                // Only set if not already set (allows override for admin import tools)
                if (string.IsNullOrEmpty(entry.Entity.TenantId))
                {
                    entry.Entity.TenantId = tenantId!;
                }
            }
        }
    }
}
