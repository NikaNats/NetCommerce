#nullable enable
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.Compliance.Pii;

/// <summary>
///     Abstract base for PII Vault entries.
///     The Vault Pattern: Instead of storing PII directly in business schemas,
///     store a ProfileId (token). The actual PII lives in a highly restricted Vault schema.
///     Benefits:
///     1. Instant Anonymization: Delete vault entry → all references become anonymous
///     2. Centralized Security: One schema to audit, monitor, and protect
///     3. GDPR "Right to be Forgotten": Delete one row, done
///     4. Least Privilege: Business modules can't access PII even with SQL injection
/// </summary>
public abstract class PiiVaultEntryBase : Entity<Guid>, IMultiTenant, ISoftDelete
{
    protected PiiVaultEntryBase()
    {
        UserId = string.Empty;
        TenantId = string.Empty;
    }

    protected PiiVaultEntryBase(Guid profileId, string userId)
    {
        Id = Guid.NewGuid();
        ProfileId = profileId;
        UserId = userId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        LastAccessedAt = DateTime.UtcNow;
        IsDeleted = false;
    }

    /// <summary>
    ///     The unique identifier for this PII profile.
    ///     This is the token stored in business schemas.
    /// </summary>
    public Guid ProfileId { get; protected set; }

    /// <summary>
    ///     The User ID who owns this PII data.
    ///     Links to Identity schema.
    /// </summary>
    public string UserId { get; protected set; }

    /// <summary>
    ///     Owning tenant. Enforces row-level isolation through the kernel
    ///     global query filter; stamped automatically on insert by
    ///     <c>TenantSaveInterceptor</c> when left empty.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    ///     Actor that performed the soft delete (GDPR audit trail).
    /// </summary>
    public string? DeletedBy { get; set; }

    /// <summary>
    ///     When this PII entry was created.
    /// </summary>
    public DateTime CreatedAt { get; protected set; }

    /// <summary>
    ///     When this PII entry was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; protected set; }

    /// <summary>
    ///     When this PII entry was last accessed (for compliance audits).
    /// </summary>
    public DateTime LastAccessedAt { get; protected set; }

    /// <summary>
    ///     The version of the encryption key used.
    ///     Enables key rotation tracking.
    /// </summary>
    public int KeyVersion { get; protected set; }

    /// <summary>
    ///     Soft delete flag for GDPR compliance.
    ///     When true, the entry is considered deleted but retained for audit.
    /// </summary>
    public bool IsDeleted { get; protected set; }

    /// <summary>
    ///     When the entry was soft deleted.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    ///     Records an access to this PII entry.
    /// </summary>
    public void RecordAccess()
    {
        LastAccessedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Marks this entry as deleted (GDPR forget).
    /// </summary>
    public virtual void MarkAsDeleted()
    {
        SoftDelete("system");
    }

    /// <summary>
    ///     <see cref="ISoftDelete"/> implementation. Forgotten entries stay
    ///     invisible to LINQ through the kernel soft-delete query filter.
    /// </summary>
    public void SoftDelete(string deletedBy)
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Restores a previously forgotten entry (e.g., erroneous erasure with
    ///     legal basis to retain).
    /// </summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Updates the key version after re-encryption.
    /// </summary>
    public void UpdateKeyVersion(int newVersion)
    {
        KeyVersion = newVersion;
        UpdatedAt = DateTime.UtcNow;
    }

    protected void MarkAsUpdated()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
