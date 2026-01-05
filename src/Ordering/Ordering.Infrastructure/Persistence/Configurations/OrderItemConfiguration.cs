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

        // Price Snapshotting - preserve the final price at order time (for backward compatibility)
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

        builder.Property(oi => oi.AppliedWeightKg)
            .HasColumnName("applied_weight_kg")
            .HasPrecision(10, 3)
            .IsRequired();

        // ============================================================================
        // Triple-Pass Pricing Pattern - Audit-Ready Price Breakdown
        // ============================================================================
        // Store complete pricing breakdown for legal compliance and transparency
        builder.OwnsOne(oi => oi.PriceBreakdown, breakdown =>
        {
            breakdown.Property(pb => pb.BasePrice)
                .HasColumnName("base_price")
                .HasPrecision(18, 2)
                .IsRequired()
                .HasComment("Original price from catalog at order time");

            breakdown.Property(pb => pb.DiscountAmount)
                .HasColumnName("discount_amount")
                .HasPrecision(18, 2)
                .IsRequired()
                .HasComment("Total discount applied from promotions and coupons");

            breakdown.Property(pb => pb.TaxAmount)
                .HasColumnName("tax_amount")
                .HasPrecision(18, 2)
                .IsRequired()
                .HasComment("Calculated tax amount based on jurisdiction");

            breakdown.Property(pb => pb.TaxRate)
                .HasColumnName("tax_rate")
                .HasPrecision(10, 4)
                .IsRequired()
                .HasComment("Tax rate applied (e.g., 0.18 for 18% VAT) - crucial for legal audits");

            breakdown.Property(pb => pb.TaxType)
                .HasColumnName("tax_type")
                .HasMaxLength(50)
                .IsRequired()
                .HasComment("Type of tax applied (VAT, SALES_TAX, GST, etc.)");

            breakdown.Property(pb => pb.Currency)
                .HasColumnName("breakdown_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        // Computed properties - not persisted
        builder.Ignore(oi => oi.LineTotal);
        builder.Ignore(oi => oi.DiscountAmount);
        builder.Ignore(oi => oi.TaxAmount);
    }
}
