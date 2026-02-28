#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Finance.Domain.Audit;

namespace NetCommerce.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
///     EF Core configuration for FinancialAuditEntry.
///     Enforces immutability and optimizes for append-only workload.
/// </summary>
public class FinancialAuditEntryConfiguration : IEntityTypeConfiguration<FinancialAuditEntry>
{
    public void Configure(EntityTypeBuilder<FinancialAuditEntry> builder)
    {
        builder.ToTable("financial_audit_log", FinanceDbContext.Schema, tb =>
        {
            // PostgreSQL: Prevent UPDATE/DELETE at database level for true immutability
            // This rule can be created via migration: CREATE RULE audit_no_update AS ON UPDATE TO finance.financial_audit_log DO INSTEAD NOTHING;
            tb.HasComment("Immutable audit log - INSERT only, no UPDATE/DELETE");
        });

        builder.HasKey(e => e.Id);

        // ═══════════════════════════════════════════════════════════════════════════
        // Indexes optimized for audit queries
        // ═══════════════════════════════════════════════════════════════════════════

        // Time-based queries (compliance reports, reconciliation)
        builder.HasIndex(e => e.OccurredAt)
            .HasDatabaseName("ix_financial_audit_occurred_at");

        // Entity lookup (forensics: "show me everything that happened to order X")
        builder.HasIndex(e => new { e.EntityType, e.EntityId })
            .HasDatabaseName("ix_financial_audit_entity");

        // External transaction lookup (stripe payment_intent tracking)
        builder.HasIndex(e => e.ExternalTransactionId)
            .HasDatabaseName("ix_financial_audit_external_txn")
            .HasFilter("external_transaction_id IS NOT NULL");

        // Correlation ID for distributed tracing
        builder.HasIndex(e => e.CorrelationId)
            .HasDatabaseName("ix_financial_audit_correlation")
            .HasFilter("correlation_id IS NOT NULL");

        // Type-based filtering
        builder.HasIndex(e => e.AuditType)
            .HasDatabaseName("ix_financial_audit_type");

        // ═══════════════════════════════════════════════════════════════════════════
        // Property configuration
        // ═══════════════════════════════════════════════════════════════════════════

        builder.Property(e => e.AuditType)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.ActorType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.EntityType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EntityId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.ExternalTransactionId)
            .HasMaxLength(100);

        builder.Property(e => e.Currency)
            .HasMaxLength(3);

        builder.Property(e => e.ActorId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .HasMaxLength(100);

        // JSON columns for state snapshots (use PostgreSQL jsonb for efficiency)
        builder.Property(e => e.PreviousState)
            .HasColumnType("jsonb");

        builder.Property(e => e.NewState)
            .HasColumnType("jsonb");

        builder.Property(e => e.Metadata)
            .HasColumnType("jsonb");

        // Amount with precision for financial accuracy
        builder.Property(e => e.Amount)
            .HasPrecision(18, 4);
    }
}
