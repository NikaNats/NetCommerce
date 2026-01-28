#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Shipping.Domain;

namespace NetCommerce.Shipping.Infrastructure.Persistence.Configurations;

/// <summary>
///     EF Core configuration for Shipment aggregate.
/// </summary>
public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments", ShippingDbContext.Schema);

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.OrderId)
            .IsRequired();

        builder.HasIndex(s => s.OrderId)
            .IsUnique();

        builder.Property(s => s.TrackingNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(s => s.TrackingNumber)
            .IsUnique();

        builder.Property(s => s.CourierProvider)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(s => s.CourierProvider);

        builder.Property(s => s.WeightKg)
            .HasPrecision(10, 2);

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasIndex(s => s.Status);

        builder.Property(s => s.FailureReason)
            .HasMaxLength(500);

        // Owned type for Dimensions
        builder.OwnsOne(s => s.Dimensions, dimensions =>
        {
            dimensions.Property(d => d.LengthCm).HasColumnName("length_cm");
            dimensions.Property(d => d.WidthCm).HasColumnName("width_cm");
            dimensions.Property(d => d.HeightCm).HasColumnName("height_cm");
        });

        // Owned type for ShippingAddress
        builder.OwnsOne(s => s.ShippingAddress, address =>
        {
            address.Property(a => a.RecipientName).HasColumnName("recipient_name").HasMaxLength(200);
            address.Property(a => a.Street).HasColumnName("street").HasMaxLength(500);
            address.Property(a => a.City).HasColumnName("city").HasMaxLength(100);
            address.Property(a => a.State).HasColumnName("state").HasMaxLength(100);
            address.Property(a => a.Country).HasColumnName("country").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("postal_code").HasMaxLength(20);
            address.Property(a => a.Phone).HasColumnName("phone").HasMaxLength(30);
        });

        // Optimistic concurrency
        builder.Property(s => s.Version)
            .IsRowVersion();

        // Composite index for tracking queries
        builder.HasIndex(s => new { s.Status, s.CreatedAt });
    }
}
