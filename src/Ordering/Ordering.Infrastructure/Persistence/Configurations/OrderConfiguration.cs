using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Ordering.Domain.Orders;

namespace NetCommerce.Ordering.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(o => o.OrderNumber)
            .HasColumnName("order_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(o => o.OrderNumber)
            .IsUnique();

        builder.Property(o => o.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.HasIndex(o => o.CustomerId);

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // TotalAmount as owned Money value object
        builder.OwnsOne(o => o.TotalAmount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("total_amount")
                .HasPrecision(18, 2)
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("total_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(o => o.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(o => o.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100);

        builder.HasIndex(o => o.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(o => o.PaidAt)
            .HasColumnName("paid_at");

        builder.Property(o => o.ShippedAt)
            .HasColumnName("shipped_at");

        builder.Property(o => o.DeliveredAt)
            .HasColumnName("delivered_at");

        builder.Property(o => o.CancelledAt)
            .HasColumnName("cancelled_at");

        builder.Property(o => o.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        // ShippingAddress as owned value object
        builder.OwnsOne(o => o.ShippingAddress, sa =>
        {
            sa.Property(a => a.RecipientName).HasColumnName("shipping_recipient").HasMaxLength(200).IsRequired();
            sa.Property(a => a.Street).HasColumnName("shipping_street").HasMaxLength(200).IsRequired();
            sa.Property(a => a.City).HasColumnName("shipping_city").HasMaxLength(100).IsRequired();
            sa.Property(a => a.State).HasColumnName("shipping_state").HasMaxLength(100);
            sa.Property(a => a.Country).HasColumnName("shipping_country").HasMaxLength(100).IsRequired();
            sa.Property(a => a.PostalCode).HasColumnName("shipping_postal_code").HasMaxLength(20);
            sa.Property(a => a.Phone).HasColumnName("shipping_phone").HasMaxLength(50);
        });

        // BillingAddress as owned value object (optional)
        builder.OwnsOne(o => o.BillingAddress, ba =>
        {
            ba.Property(a => a.Name).HasColumnName("billing_name").HasMaxLength(200);
            ba.Property(a => a.Street).HasColumnName("billing_street").HasMaxLength(200);
            ba.Property(a => a.City).HasColumnName("billing_city").HasMaxLength(100);
            ba.Property(a => a.State).HasColumnName("billing_state").HasMaxLength(100);
            ba.Property(a => a.Country).HasColumnName("billing_country").HasMaxLength(100);
            ba.Property(a => a.PostalCode).HasColumnName("billing_postal_code").HasMaxLength(20);
        });

        // OrderItems
        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey("order_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}