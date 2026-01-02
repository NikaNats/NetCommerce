using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Entity Framework Core configuration for IntegrationEventLog.
/// </summary>
public class IntegrationEventLogConfiguration : IEntityTypeConfiguration<IntegrationEventLog>
{
    public void Configure(EntityTypeBuilder<IntegrationEventLog> builder)
    {
        builder.ToTable("integration_event_logs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.Content)
            .HasColumnName("content")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.OccurredOn)
            .HasColumnName("occurred_on")
            .IsRequired();

        builder.Property(e => e.LoggedAt)
            .HasColumnName("logged_at")
            .IsRequired();

        builder.Property(e => e.Direction)
            .HasColumnName("direction")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(100);

        builder.Property(e => e.TraceId)
            .HasColumnName("trace_id")
            .HasMaxLength(32);

        builder.Property(e => e.SpanId)
            .HasColumnName("span_id")
            .HasMaxLength(16);

        builder.Property(e => e.HandlerName)
            .HasColumnName("handler_name")
            .HasMaxLength(500);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.Error)
            .HasColumnName("error")
            .HasMaxLength(2000);

        builder.Property(e => e.ProcessedAt)
            .HasColumnName("processed_at");

        builder.Property(e => e.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(e => e.TimesSent)
            .HasColumnName("times_sent")
            .HasDefaultValue(0);

        // Indexes for common queries
        builder.HasIndex(e => e.EventId)
            .HasDatabaseName("ix_integration_event_logs_event_id");

        builder.HasIndex(e => e.EventType)
            .HasDatabaseName("ix_integration_event_logs_event_type");

        builder.HasIndex(e => e.OccurredOn)
            .HasDatabaseName("ix_integration_event_logs_occurred_on");

        builder.HasIndex(e => e.LoggedAt)
            .HasDatabaseName("ix_integration_event_logs_logged_at");

        builder.HasIndex(e => new { e.Direction, e.Status })
            .HasDatabaseName("ix_integration_event_logs_direction_status");

        builder.HasIndex(e => e.CorrelationId)
            .HasDatabaseName("ix_integration_event_logs_correlation_id");

        builder.HasIndex(e => e.TraceId)
            .HasDatabaseName("ix_integration_event_logs_trace_id");

        // Composite index for common audit queries
        builder.HasIndex(e => new { e.EventType, e.Direction, e.OccurredOn })
            .HasDatabaseName("ix_integration_event_logs_type_direction_occurred");
    }

}