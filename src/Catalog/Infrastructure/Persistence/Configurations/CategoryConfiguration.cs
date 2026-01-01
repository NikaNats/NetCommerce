using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Catalog.Domain.Categories;

namespace NetCommerce.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.Slug)
            .IsRequired()
            .HasMaxLength(150);

        builder.HasIndex(c => c.Slug)
            .IsUnique();

        builder.Property(c => c.ParentCategoryId);

        builder.HasIndex(c => c.ParentCategoryId);

        builder.Property(c => c.DisplayOrder);

        builder.Property(c => c.IsActive)
            .HasDefaultValue(true);

        builder.Property(c => c.ImageKey)
            .HasMaxLength(500);

        // Self-referencing relationship for hierarchy
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency
        builder.Property(c => c.Version)
            .IsRowVersion();
    }
}