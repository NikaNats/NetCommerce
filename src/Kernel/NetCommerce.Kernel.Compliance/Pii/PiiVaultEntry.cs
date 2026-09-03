#nullable enable
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.Compliance.Pii;

/// <summary>
///     2025 Elite Pattern: PII Vault Entry - The Confidential Data Vault.
///     The Vault Pattern:
///     Instead of storing PII (personally identifiable information) directly in business schemas,
///     we store a ProfileId (token). The actual PII lives in a highly restricted Vault schema.
///     Benefits:
///     1. Instant Anonymization: Delete vault entry → all references become anonymous
///     2. Centralized Security: One schema to audit, monitor, and protect
///     3. GDPR "Right to be Forgotten": Delete one row, done
///     4. Least Privilege: Catalog/Media modules can't access PII even with SQL injection
///     5. Audit Trail: All PII access is logged at vault level
/// </summary>
public sealed class PiiVaultEntry : PiiVaultEntryBase
{
    // EF Core constructor
    private PiiVaultEntry()
    {
        EncryptedFullName = string.Empty;
        EncryptedEmail = string.Empty;
        EmailBlindIndex = string.Empty;
        EncryptedPhoneNumber = string.Empty;
        PhoneBlindIndex = string.Empty;
        EncryptedAddress = string.Empty;
    }

    private PiiVaultEntry(
        Guid profileId,
        string userId,
        string encryptedFullName,
        string encryptedEmail,
        string emailBlindIndex,
        string encryptedPhoneNumber,
        string phoneBlindIndex,
        string encryptedAddress,
        string? encryptedDateOfBirth,
        string? encryptedNationalId,
        int keyVersion,
        string? tenantId)
    {
        Id = Guid.NewGuid();
        ProfileId = profileId;
        UserId = userId;
        TenantId = tenantId ?? string.Empty;
        EncryptedFullName = encryptedFullName;
        EncryptedEmail = encryptedEmail;
        EmailBlindIndex = emailBlindIndex;
        EncryptedPhoneNumber = encryptedPhoneNumber;
        PhoneBlindIndex = phoneBlindIndex;
        EncryptedAddress = encryptedAddress;
        EncryptedDateOfBirth = encryptedDateOfBirth;
        EncryptedNationalId = encryptedNationalId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        LastAccessedAt = DateTime.UtcNow;
        KeyVersion = keyVersion;
        IsDeleted = false;
    }

    // Identity, ownership, tenancy, audit timestamps, key version and soft-delete
    // state are inherited from PiiVaultEntryBase (single source of truth).

    /// <summary>
    ///     Full name encrypted with envelope encryption.
    /// </summary>
    public string EncryptedFullName { get; private set; }

    /// <summary>
    ///     Email address encrypted with deterministic encryption.
    /// </summary>
    public string EncryptedEmail { get; private set; }

    /// <summary>
    ///     Blind index for searching by email without decryption.
    /// </summary>
    public string EmailBlindIndex { get; private set; }

    /// <summary>
    ///     Phone number encrypted with deterministic encryption.
    /// </summary>
    public string EncryptedPhoneNumber { get; private set; }

    /// <summary>
    ///     Blind index for searching by phone without decryption.
    /// </summary>
    public string PhoneBlindIndex { get; private set; }

    /// <summary>
    ///     Shipping address encrypted with probabilistic encryption.
    /// </summary>
    public string EncryptedAddress { get; private set; }

    /// <summary>
    ///     Date of birth encrypted for age verification (COPPA compliance).
    /// </summary>
    public string? EncryptedDateOfBirth { get; private set; }

    /// <summary>
    ///     National ID / SSN / Tax ID encrypted with maximum security.
    /// </summary>
    public string? EncryptedNationalId { get; private set; }

    /// <summary>
    ///     Creates a new PII vault entry with encrypted data.
    /// </summary>
    public static PiiVaultEntry Create(
        Guid profileId,
        string userId,
        string encryptedFullName,
        string encryptedEmail,
        string emailBlindIndex,
        string encryptedPhoneNumber,
        string phoneBlindIndex,
        string encryptedAddress,
        string? encryptedDateOfBirth = null,
        string? encryptedNationalId = null,
        int keyVersion = 1,
        string? tenantId = null)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("Profile ID cannot be empty.", nameof(profileId));

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        if (string.IsNullOrWhiteSpace(encryptedFullName))
            throw new ArgumentException("Encrypted full name is required.", nameof(encryptedFullName));

        if (string.IsNullOrWhiteSpace(encryptedEmail))
            throw new ArgumentException("Encrypted email is required.", nameof(encryptedEmail));

        if (string.IsNullOrWhiteSpace(emailBlindIndex))
            throw new ArgumentException("Email blind index is required.", nameof(emailBlindIndex));

        return new PiiVaultEntry(
            profileId,
            userId,
            encryptedFullName,
            encryptedEmail,
            emailBlindIndex,
            encryptedPhoneNumber,
            phoneBlindIndex,
            encryptedAddress,
            encryptedDateOfBirth,
            encryptedNationalId,
            keyVersion,
            tenantId);
    }

    /// <summary>
    ///     Updates PII data (requires re-encryption).
    /// </summary>
    public void Update(
        string encryptedFullName,
        string encryptedEmail,
        string emailBlindIndex,
        string encryptedPhoneNumber,
        string phoneBlindIndex,
        string encryptedAddress)
    {
        EncryptedFullName = encryptedFullName;
        EncryptedEmail = encryptedEmail;
        EmailBlindIndex = emailBlindIndex;
        EncryptedPhoneNumber = encryptedPhoneNumber;
        PhoneBlindIndex = phoneBlindIndex;
        EncryptedAddress = encryptedAddress;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Marks this PII entry as deleted (GDPR "Right to be Forgotten").
    /// </summary>
    public override void MarkAsDeleted()
    {
        if (IsDeleted)
            throw new InvalidOperationException("PII entry is already deleted.");

        base.MarkAsDeleted();
    }

    /// <summary>
    ///     Physically removes all PII data (hard delete after audit period).
    /// </summary>
    public void PurgeData()
    {
        if (!IsDeleted)
            throw new InvalidOperationException("Cannot purge data that is not marked as deleted.");

        // Overwrite PII with random data
        EncryptedFullName = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        EncryptedEmail = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        EmailBlindIndex = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        EncryptedPhoneNumber = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        PhoneBlindIndex = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        EncryptedAddress = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        EncryptedDateOfBirth = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        EncryptedNationalId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        // Break the ProfileId link
        ProfileId = Guid.Empty;
    }

    /// <summary>
    ///     Re-encrypts all PII fields with a new key version (key rotation).
    /// </summary>
    public void ReEncrypt(
        string newEncryptedFullName,
        string newEncryptedEmail,
        string newEncryptedPhoneNumber,
        string newEncryptedAddress,
        string? newEncryptedDateOfBirth,
        string? newEncryptedNationalId,
        int newKeyVersion)
    {
        EncryptedFullName = newEncryptedFullName;
        EncryptedEmail = newEncryptedEmail;
        EncryptedPhoneNumber = newEncryptedPhoneNumber;
        EncryptedAddress = newEncryptedAddress;
        EncryptedDateOfBirth = newEncryptedDateOfBirth;
        EncryptedNationalId = newEncryptedNationalId;
        KeyVersion = newKeyVersion;
        UpdatedAt = DateTime.UtcNow;
    }
}
