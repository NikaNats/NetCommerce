using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.EfCore.Persistence;

namespace NetCommerce.Payments.Infrastructure.Persistence;

[UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
    Justification = "EF Core's ApplyConfigurationsFromAssembly uses reflection by design. All entity configurations are in this assembly.")]
public class PaymentsDbContext : BaseDbContext
{
    public const string Schema = "payments";

    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
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
