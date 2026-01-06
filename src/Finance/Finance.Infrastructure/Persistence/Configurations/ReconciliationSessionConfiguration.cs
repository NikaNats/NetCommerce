using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Finance.Domain.Reconciliation;

namespace NetCommerce.Finance.Infrastructure.Persistence.Configurations;

public class ReconciliationSessionConfiguration : IEntityTypeConfiguration<ReconciliationSession>
{
    public void Configure(EntityTypeBuilder<ReconciliationSession> builder)
    {
        builder.ToTable("ReconciliationSessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CalculatedForDate)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.TotalInternalAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.TotalExternalAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.CompletedAt);

        builder.Property(x => x.Notes)
            .HasMaxLength(2000);

        // Configure owned collection for Discrepancies
        builder.OwnsMany(x => x.Discrepancies, discrepancy =>
        {
            discrepancy.ToTable("ReconciliationDiscrepancies");

            discrepancy.WithOwner().HasForeignKey("ReconciliationSessionId");

            discrepancy.HasKey("ReconciliationSessionId", "ExternalTxnId", "DetectedAt");

            discrepancy.Property(d => d.ExternalTxnId)
                .HasMaxLength(100);

            discrepancy.Property(d => d.Type)
                .IsRequired()
                .HasConversion<string>();

            discrepancy.Property(d => d.Difference)
                .HasPrecision(18, 2);

            discrepancy.Property(d => d.Reason)
                .HasMaxLength(500);

            discrepancy.Property(d => d.DetectedAt)
                .IsRequired();
        });

        // Index for performance
        builder.HasIndex(x => x.CalculatedForDate);
        builder.HasIndex(x => x.Status);
    }
}
