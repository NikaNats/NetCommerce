using NetCommerce.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetCommerce.Inventory.Infrastructure.Persistence.Configurations;

public class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");

        builder.HasKey(sr => sr.Id);
        
        builder.Property(sr => sr.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(sr => sr.StockId)
            .HasColumnName("stock_id")
            .IsRequired();

        builder.Property(sr => sr.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.HasIndex(sr => sr.OrderId);

        builder.Property(sr => sr.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(sr => sr.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(sr => sr.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(sr => sr.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(sr => sr.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.Property(sr => sr.ReleasedAt)
            .HasColumnName("released_at");

        // Index for finding expired reservations
        builder.HasIndex(sr => new { sr.Status, sr.ExpiresAt })
            .HasFilter("status = 'Active'");
    }
}

