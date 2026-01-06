#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     EF Core interceptor for automatic audit field updates.
///     Modern replacement for overriding SaveChangesAsync.
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            UpdateAuditableEntities(eventData.Context);
            UpdateSoftDeletedEntities(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            UpdateAuditableEntities(eventData.Context);
            UpdateSoftDeletedEntities(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private static void UpdateAuditableEntities(DbContext context)
    {
        var entries = context.ChangeTracker.Entries<IAuditableEntity>();
        var utcNow = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = utcNow;

            if (entry.State == EntityState.Modified)
                entry.Entity.ModifiedAt = utcNow;
        }
    }

    private static void UpdateSoftDeletedEntities(DbContext context)
    {
        var entries = context.ChangeTracker.Entries<NetCommerce.Kernel.Core.Domain.ISoftDelete>();
        var utcNow = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Deleted)
            {
                // Convert hard delete to soft delete
                entry.State = EntityState.Modified;
                entry.Entity.DeletedAt = utcNow;
                // DeletedBy should be set by the caller via SoftDelete(userId)
            }
        }
    }
}
