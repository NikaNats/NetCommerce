using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(o => o.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(o => o.Type)
            .HasColumnName("type")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(o => o.Content)
            .HasColumnName("content")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(o => o.OccurredOn)
            .HasColumnName("occurred_on")
            .IsRequired();

        builder.Property(o => o.ProcessedOn)
            .HasColumnName("processed_on");

        builder.Property(o => o.Error)
            .HasColumnName("error")
            .HasMaxLength(2000);

        builder.Property(o => o.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0);

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasDefaultValue(OutboxMessageStatus.Pending)
            .IsRequired();

        builder.Property(o => o.ProcessingStartedAt)
            .HasColumnName("processing_started_at");

        // Index for SELECT FOR UPDATE SKIP LOCKED query - targets Pending messages and stuck Processing messages
        builder.HasIndex(o => new { o.Status, o.OccurredOn })
            .HasDatabaseName("ix_outbox_messages_status_occurred");

        // Index for detecting stuck messages that can be reclaimed
        builder.HasIndex(o => new { o.Status, o.ProcessingStartedAt })
            .HasFilter($"status = {(int)OutboxMessageStatus.Processing}")
            .HasDatabaseName("ix_outbox_messages_stuck");
    }
}