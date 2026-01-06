using Microsoft.EntityFrameworkCore;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Kernel.EfCore.Persistence;

namespace NetCommerce.Ordering.Infrastructure.Persistence;

public class OrderingDbContext : BaseDbContext
{
    public const string Schema = "ordering";

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options)
        : base(options)
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
