#region

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.SharedKernel.Domain;

#endregion

namespace NetCommerce.SharedKernel.Infrastructure.Persistence;

/// <summary>
///     2025 Elite Pattern: EF Core configuration for the Immutable Audit Ledger.
///     Design Decisions:
///     1. Separate table (not mixed with domain entities)
///     2. Append-only (enforced at DB permission level)
///     3. High-performance indexes for common queries
///     4. Partitioning strategy for large-scale systems
///     5. Retention policy (archive to cold storage after N years)
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

        // Primary key
        builder.HasKey(a => a.Id);

        // Timestamp - the PRIMARY sorting dimension
        builder.Property(a => a.Timestamp)
            .HasColumnName("timestamp")
            .IsRequired()
            .HasComment("UTC timestamp when the action occurred. PRIMARY sorting key.");

        // WHO did it?
        builder.Property(a => a.UserId)
            .HasColumnName("user_id")
            .HasMaxLength(100)
            .IsRequired()
            .HasComment("User ID from JWT claims or API key");

        builder.Property(a => a.UserRole)
            .HasColumnName("user_role")
            .HasMaxLength(50)
            .IsRequired()
            .HasComment("Role at the time of action (not current role)");

        // WHAT happened?
        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasMaxLength(200)
            .IsRequired()
            .HasComment("Semantic business action: {Module}.{Action}");

        builder.Property(a => a.ResourceId)
            .HasColumnName("resource_id")
            .HasMaxLength(100)
            .IsRequired()
            .HasComment("Business entity ID (OrderId, ProductId, etc.)");

        builder.Property(a => a.Module)
            .HasColumnName("module")
            .HasMaxLength(50)
            .IsRequired()
            .HasComment("Bounded context: Ordering, Catalog, Payments, etc.");

        // WHY did it happen?
        builder.Property(a => a.Context)
            .HasColumnName("context")
            .HasColumnType("jsonb") // PostgreSQL JSONB for queryable JSON
            .IsRequired()
            .HasComment("Business intent as JSON: { Reason, OldValue, NewValue }");

        // Link to technical observability
        builder.Property(a => a.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(100)
            .IsRequired()
            .HasComment("Link to Seq/OpenTelemetry traces");

        // Optional security context
        builder.Property(a => a.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(50)
            .HasComment("IP address from which action was performed");

        builder.Property(a => a.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500)
            .HasComment("Browser/API client that initiated the action");

        // ============================================================================
        // 2025 Elite Indexing Strategy
        // ============================================================================

        // Index 1: Timeline View - "Show me all actions on Order #12345"
        builder.HasIndex(a => new { a.ResourceId, a.Timestamp })
            .HasDatabaseName("ix_audit_logs_resource_timeline");

        // Index 2: Module Query - "Show me all Ordering actions in December"
        builder.HasIndex(a => new { a.Module, a.Timestamp })
            .HasDatabaseName("ix_audit_logs_module_timeline");

        // Index 3: Action Query - "Show me all Order.Cancelled actions"
        builder.HasIndex(a => new { a.Action, a.Timestamp })
            .HasDatabaseName("ix_audit_logs_action_timeline");

        // Index 4: User Audit - "Show me all actions by admin_123"
        builder.HasIndex(a => new { a.UserId, a.Timestamp })
            .HasDatabaseName("ix_audit_logs_user_timeline");

        // Index 5: Correlation - "Find audit entry linked to trace abc-123"
        builder.HasIndex(a => a.CorrelationId)
            .HasDatabaseName("ix_audit_logs_correlation");

        // Index 6: Full-text search on Context (PostgreSQL GIN index)
        // Uncomment if you need to search within JSON context
        // builder.HasIndex(a => a.Context)
        //     .HasMethod("gin")
        //     .HasDatabaseName("ix_audit_logs_context_gin");
    }
}
