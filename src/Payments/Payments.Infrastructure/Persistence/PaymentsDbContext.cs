using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Payments.Infrastructure.Persistence;

public class PaymentsDbContext : BaseDbContext
{
    public const string Schema = "payments";

    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, IMediator mediator)
        : base(options, mediator)
    {
    }

    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}