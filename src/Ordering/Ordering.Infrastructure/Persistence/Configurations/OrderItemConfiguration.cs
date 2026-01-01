using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Ordering.Domain.Orders;

namespace NetCommerce.Ordering.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(oi => oi.Id);

        builder.Property(oi => oi.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(oi => oi.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        // Price Snapshotting - preserve the title at order time
        builder.Property(oi => oi.AppliedTitle)
            .HasColumnName("applied_title")
            .HasMaxLength(500)
            .IsRequired();

        // Price Snapshotting - preserve the price at order time
        builder.OwnsOne(oi => oi.AppliedPrice, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("applied_price_amount")
                .HasPrecision(18, 2)
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("applied_price_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(oi => oi.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(oi => oi.Sku)
            .HasColumnName("sku")
            .HasMaxLength(100);

        // LineTotal is a computed property, not persisted
        builder.Ignore(oi => oi.LineTotal);
    }
}