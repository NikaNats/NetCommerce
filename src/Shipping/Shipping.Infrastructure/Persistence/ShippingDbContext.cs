#nullable enable

using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.EfCore.Persistence;
using NetCommerce.Shipping.Domain;

namespace NetCommerce.Shipping.Infrastructure.Persistence;

/// <summary>
///     DbContext for the Shipping module.
///     Manages shipment persistence with isolated schema.
/// </summary>
public sealed class ShippingDbContext : BaseDbContext
{
    public const string Schema = "shipping";

    public ShippingDbContext(DbContextOptions<ShippingDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShippingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
