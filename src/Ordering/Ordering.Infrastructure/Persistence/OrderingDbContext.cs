using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Domain.Outbox;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Ordering.Infrastructure.Persistence;

public class OrderingDbContext : BaseDbContext
{
    public const string Schema = "ordering";
    
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options, IMediator mediator) 
        : base(options, mediator)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
    }
}

