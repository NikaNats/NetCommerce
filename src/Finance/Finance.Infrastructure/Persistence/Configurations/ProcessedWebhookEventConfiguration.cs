#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Finance.Domain.Webhooks;

namespace NetCommerce.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
///     EF Core configuration for ProcessedWebhookEvent.
///     Uses PostgreSQL-specific features for optimal idempotency handling.
/// </summary>
public class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> builder)
    {
        builder.ToTable("processed_webhook_events", FinanceDbContext.Schema);

        builder.HasKey(e => e.Id);

        // CRITICAL: Unique constraint on Stripe event ID for atomic idempotency
        builder.HasIndex(e => e.StripeEventId)
            .IsUnique()
            .HasDatabaseName("ix_processed_webhook_events_stripe_event_id");

        // Index for purge queries (retention enforcement)
        builder.HasIndex(e => e.ReceivedAt)
            .HasDatabaseName("ix_processed_webhook_events_received_at");

        // Index for debugging/audit queries
        builder.HasIndex(e => e.PaymentIntentId)
            .HasDatabaseName("ix_processed_webhook_events_payment_intent_id");

        builder.Property(e => e.StripeEventId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.PaymentIntentId)
            .HasMaxLength(100);

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(1000);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20);
    }
}
