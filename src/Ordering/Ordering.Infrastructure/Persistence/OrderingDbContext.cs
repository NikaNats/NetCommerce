using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.EfCore.Persistence;

namespace NetCommerce.Ordering.Infrastructure.Persistence;

[UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
    Justification = "EF Core's ApplyConfigurationsFromAssembly uses reflection by design. All entity configurations are in this assembly.")]
public class OrderingDbContext : BaseDbContext
{
    public const string Schema = "ordering";

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
    }
}
