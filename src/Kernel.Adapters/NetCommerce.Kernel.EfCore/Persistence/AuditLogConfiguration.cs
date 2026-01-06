#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Kernel.Compliance.Audit;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     EF Core configuration for the Immutable Audit Ledger.
/// </summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_logs", t =>
        {
            t.HasComment("Immutable business event store for legal compliance. " +
                         "NO UPDATE or DELETE permissions for application user.");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Timestamp)
            .HasColumnName("timestamp")
            .IsRequired()
            .HasComment("UTC timestamp when the action occurred.");

        builder.Property(a => a.UserId)
            .HasColumnName("user_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.UserRole)
            .HasColumnName("user_role")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.ResourceId)
            .HasColumnName("resource_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.Module)
            .HasColumnName("module")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Context)
            .HasColumnName("context")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(a => a.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500);

        // Indexes for common query patterns
        builder.HasIndex(a => a.ResourceId).HasDatabaseName("ix_audit_logs_resource_id");
        builder.HasIndex(a => a.Module).HasDatabaseName("ix_audit_logs_module");
        builder.HasIndex(a => a.Timestamp).HasDatabaseName("ix_audit_logs_timestamp");
        builder.HasIndex(a => new { a.Module, a.Action }).HasDatabaseName("ix_audit_logs_module_action");
        builder.HasIndex(a => new { a.ResourceId, a.Module }).HasDatabaseName("ix_audit_logs_resource_module");
    }
}
