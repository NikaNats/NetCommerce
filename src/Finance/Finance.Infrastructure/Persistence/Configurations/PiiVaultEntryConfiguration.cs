#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCommerce.Kernel.Compliance.Pii;

namespace NetCommerce.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
///     EF Core configuration for the PII vault.
///     The vault lives in the <c>finance</c> schema (restricted, audited) rather
///     than alongside business tables. Blind-index lookups are composite-unique
///     per tenant: the same email in two tenants yields the same deterministic
///     index, so uniqueness must never be global on the index alone.
/// </summary>
public class PiiVaultEntryConfiguration : IEntityTypeConfiguration<PiiVaultEntry>
{
    public void Configure(EntityTypeBuilder<PiiVaultEntry> builder)
    {
        builder.ToTable("pii_vault_entries", FinanceDbContext.Schema);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProfileId)
            .IsRequired();
        builder.HasIndex(e => e.ProfileId)
            .IsUnique()
            .HasDatabaseName("ix_pii_vault_entries_profile_id");

        builder.Property(e => e.UserId)
            .HasMaxLength(256)
            .IsRequired();
        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("ix_pii_vault_entries_user_id");

        builder.Property(e => e.TenantId)
            .HasMaxLength(128)
            .IsRequired();

        // Encrypted payloads use envelope/storage formats whose length depends
        // on plaintext size; text (unbounded) avoids truncation of PII.
        builder.Property(e => e.EncryptedFullName).IsRequired();
        builder.Property(e => e.EncryptedEmail).IsRequired();
        builder.Property(e => e.EncryptedPhoneNumber).IsRequired();
        builder.Property(e => e.EncryptedAddress).IsRequired();

        builder.Property(e => e.EmailBlindIndex)
            .HasMaxLength(256)
            .IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.EmailBlindIndex })
            .IsUnique()
            .HasDatabaseName("ix_pii_vault_entries_tenant_email_index");

        builder.Property(e => e.PhoneBlindIndex)
            .HasMaxLength(256)
            .IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.PhoneBlindIndex })
            .IsUnique()
            .HasDatabaseName("ix_pii_vault_entries_tenant_phone_index");

        builder.Property(e => e.KeyVersion)
            .IsRequired();

        builder.Property(e => e.DeletedBy)
            .HasMaxLength(256);
    }
}
