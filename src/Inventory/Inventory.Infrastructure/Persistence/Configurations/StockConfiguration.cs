using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Inventory.Domain.Stock;

namespace NetCommerce.Inventory.Infrastructure.Persistence.Configurations;

public class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("stocks");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.HasIndex(s => s.ProductId)
            .IsUnique();

        builder.Property(s => s.Sku)
            .HasColumnName("sku")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(s => s.Sku)
            .IsUnique();

        builder.Property(s => s.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(s => s.LowStockThreshold)
            .HasColumnName("low_stock_threshold")
            .HasDefaultValue(10);

        builder.Property(s => s.WarehouseLocation)
            .HasColumnName("warehouse_location")
            .HasMaxLength(200);

        builder.Property(s => s.LastUpdatedAt)
            .HasColumnName("last_updated_at")
            .IsRequired();

        // -------------------------------------------------------------
        // CONCURRENCY CONFIGURATION (Fixed for SQLite Tests)
        // -------------------------------------------------------------
        builder.Property(s => s.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion()       // Tells EF: "DB handles this, check it on updates"
            .HasDefaultValue(0);  // FIX: Allows SQLite to insert '0' when EF omits the column

        // Computed properties - not persisted
        builder.Ignore(s => s.AvailableQuantity);
        builder.Ignore(s => s.ReservedQuantity);
        builder.Ignore(s => s.IsLowStock);

        // Reservations
        builder.HasMany(s => s.Reservations)
            .WithOne()
            .HasForeignKey(r => r.StockId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
