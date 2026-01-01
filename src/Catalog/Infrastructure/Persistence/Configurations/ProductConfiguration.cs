using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Catalog.Domain.Products;
using NpgsqlTypes;

namespace NetCommerce.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .IsRequired()
            .HasMaxLength(5000);

        builder.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.Sku)
            .IsUnique();

        builder.Property(p => p.Slug)
            .HasMaxLength(250);

        builder.HasIndex(p => p.Slug);

        // Money value object mapping
        builder.OwnsOne(p => p.Price, priceBuilder =>
        {
            priceBuilder.Property(m => m.Amount)
                .HasColumnName("price")
                .HasPrecision(18, 2)
                .IsRequired();

            priceBuilder.Property(m => m.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(p => p.CategoryId)
            .IsRequired();

        builder.HasIndex(p => p.CategoryId);

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(p => p.SeoTitle)
            .HasMaxLength(70);

        builder.Property(p => p.SeoDescription)
            .HasMaxLength(160);

        // Full-text search vector - PostgreSQL specific shadow property
        // This is a shadow property (not mapped to a CLR property) used for full-text search
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasColumnType("tsvector");

        builder.HasIndex("SearchVector")
            .HasMethod("GIN");

        // Images owned collection
        builder.OwnsMany(p => p.Images, imageBuilder =>
        {
            imageBuilder.ToTable("product_images");

            imageBuilder.WithOwner().HasForeignKey("ProductId");

            imageBuilder.HasKey(i => i.Id);

            imageBuilder.Property(i => i.ImageKey)
                .IsRequired()
                .HasMaxLength(500);

            imageBuilder.Property(i => i.DisplayOrder);
            imageBuilder.Property(i => i.IsPrimary);
        });

        // Attributes owned collection (stored as JSON)
        builder.OwnsMany(p => p.Attributes, attrBuilder =>
        {
            attrBuilder.ToTable("product_attributes");

            attrBuilder.WithOwner().HasForeignKey("ProductId");

            attrBuilder.Property(a => a.Key)
                .IsRequired()
                .HasMaxLength(100);

            attrBuilder.Property(a => a.Value)
                .IsRequired()
                .HasMaxLength(500);

            attrBuilder.Property(a => a.DisplayName)
                .HasMaxLength(100);

            attrBuilder.HasKey("ProductId", "Key");
        });

        // Optimistic concurrency
        builder.Property(p => p.Version)
            .IsRowVersion();
    }
}