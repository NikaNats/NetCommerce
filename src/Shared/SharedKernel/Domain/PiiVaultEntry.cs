namespace NetCommerce.SharedKernel.Domain;

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
///     Database Schema:
///     Vault Schema (HIGHLY RESTRICTED):
///     - pii_vault table with column-level encryption
///     - Only Identity service can write
///     - Only authenticated services with valid JWT can read
///     - DBA cannot SELECT * without audit log entry
///     Business Schema (Ordering, Payments):
///     - Stores ProfileId (Guid token)
///     - ProfileId → Vault lookup → Decrypt PII
///     - If Vault entry deleted, ProfileId returns null (anonymous order)
///     Example:
///     Order #123 has ProfileId = "abc-def-456"
///     Vault has: ProfileId "abc-def-456" → { Name: "Alice", Phone: "555-1234" }
///     User requests "Forget Me":
///     DELETE FROM pii_vault WHERE profile_id = 'abc-def-456'
///     Result:
///     - Order #123 still exists (financial audit)
///     - Order #123.ProfileId = "abc-def-456" (still links)
///     - Vault lookup returns null → Order is now anonymous
/// </summary>
public sealed class PiiVaultEntry : Entity<Guid>
{
    // EF Core constructor
    private PiiVaultEntry()
    {
        UserId = string.Empty;
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
        int keyVersion)
    {
        Id = Guid.NewGuid();
        ProfileId = profileId;
        UserId = userId;
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

    /// <summary>
    ///     The unique identifier for this PII profile.
    ///     This is the token stored in business schemas (Ordering, Payments).
    ///     CRITICAL: This is NOT the User ID (authentication).
    ///     Multiple users can share a ProfileId (family members, business accounts).
    /// </summary>
    public Guid ProfileId { get; private set; }

    /// <summary>
    ///     The User ID who owns this PII data (for authentication/authorization).
    ///     Links to Identity schema (AspNetUsers, Auth0, etc.).
    /// </summary>
    public string UserId { get; private set; }

    /// <summary>
    ///     Full name encrypted with envelope encryption.
    ///     Format: "KeyId|IV|Ciphertext|EncryptedDEK"
    /// </summary>
    public string EncryptedFullName { get; private set; }

    /// <summary>
    ///     Email address encrypted with deterministic encryption (enables exact match searches).
    /// </summary>
    public string EncryptedEmail { get; private set; }

    /// <summary>
    ///     Blind index for searching by email without decryption.
    ///     Format: Base64(HMAC-SHA256(email + salt))
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
    ///     Shipping address encrypted with probabilistic encryption (max security, no search needed).
    /// </summary>
    public string EncryptedAddress { get; private set; }

    /// <summary>
    ///     Date of birth encrypted for age verification (COPPA compliance).
    ///     Stored as encrypted ISO 8601 string.
    /// </summary>
    public string? EncryptedDateOfBirth { get; private set; }

    /// <summary>
    ///     National ID / SSN / Tax ID encrypted with maximum security.
    ///     This field uses a separate Master Key with HSM backing.
    /// </summary>
    public string? EncryptedNationalId { get; private set; }

    /// <summary>
    ///     When this PII entry was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    ///     When this PII entry was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    ///     When this PII entry was last accessed (for compliance audits).
    /// </summary>
    public DateTime LastAccessedAt { get; private set; }

    /// <summary>
    ///     The encryption key version used for this entry.
    ///     Enables key rotation without immediate data migration.
    /// </summary>
    public int KeyVersion { get; private set; }

    /// <summary>
    ///     Soft delete flag for GDPR compliance.
    ///     When true, the entry is logically deleted but kept for audit period (e.g., 90 days).
    ///     After audit period, a background job physically deletes the row.
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    ///     When the user requested to be forgotten (GDPR Article 17).
    /// </summary>
    public DateTime? DeletedAt { get; private set; }

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
        int keyVersion = 1)
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
            keyVersion);
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
    ///     Records that this PII entry was accessed (for compliance audits).
    ///     GDPR Article 15: Data subjects have the right to know who accessed their data.
    ///     This timestamp enables compliance reporting.
    /// </summary>
    public void RecordAccess()
    {
        LastAccessedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Marks this PII entry as deleted (GDPR "Right to be Forgotten").
    ///     Soft Delete Strategy:
    ///     1. Set IsDeleted = true, DeletedAt = now
    ///     2. Keep encrypted data for audit period (e.g., 90 days per legal retention)
    ///     3. Background job physically deletes after audit period
    ///     4. Business schemas see ProfileId but get null from vault lookup (anonymous)
    ///     Why Soft Delete?
    ///     - Compliance: Some jurisdictions require audit trail retention
    ///     - Fraud Prevention: Need to prove user existed during investigation period
    ///     - Reversibility: Can restore if deletion was accidental (within audit period)
    /// </summary>
    public void MarkAsDeleted()
    {
        if (IsDeleted)
            throw new InvalidOperationException("PII entry is already deleted.");

        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Physically removes all PII data (hard delete after audit period).
    ///     CRITICAL: This is irreversible. Only call from background job after audit period.
    ///     Process:
    ///     1. Overwrite all encrypted fields with random data
    ///     2. Set ProfileId to Empty Guid (break the link)
    ///     3. Database row can now be deleted
    /// </summary>
    public void PurgeData()
    {
        if (!IsDeleted)
            throw new InvalidOperationException("Cannot purge data that is not marked as deleted.");

        // Overwrite PII with random data (defense in depth)
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
