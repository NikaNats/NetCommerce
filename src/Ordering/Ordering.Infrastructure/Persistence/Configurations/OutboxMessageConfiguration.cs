using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Ordering.Domain.Outbox;

namespace NetCommerce.Ordering.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.Id);
        
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

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

        // Index for processing unprocessed messages
        builder.HasIndex(o => o.ProcessedOn)
            .HasFilter("processed_on IS NULL");
    }
}

