using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Payments.Domain.Transactions;

namespace NetCommerce.Payments.Infrastructure.Persistence.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("payment_transactions");

        builder.HasKey(pt => pt.Id);
        
        builder.Property(pt => pt.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(pt => pt.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.HasIndex(pt => pt.OrderId);

        // Amount as owned Money value object
        builder.OwnsOne(pt => pt.Amount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("amount")
                .HasPrecision(18, 2)
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(pt => pt.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(pt => pt.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(pt => pt.ExternalTransactionId)
            .HasColumnName("external_transaction_id")
            .HasMaxLength(256);

        builder.HasIndex(pt => pt.ExternalTransactionId)
            .HasFilter("external_transaction_id IS NOT NULL");

        builder.Property(pt => pt.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(1000);

        builder.Property(pt => pt.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100);

        builder.HasIndex(pt => pt.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");

        // Metadata stored as JSON
        builder.Property(pt => pt.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(pt => pt.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(pt => pt.CompletedAt)
            .HasColumnName("completed_at");
    }
}

