#nullable enable

using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     2025 Elite Pattern: Key Management Service abstraction.
///
///     This interface abstracts Azure Key Vault, AWS KMS, HashiCorp Vault, or custom HSM.
///
///     Key Management Responsibilities:
///     1. Master Key (KEK) storage and rotation
///     2. Data Key (DEK) generation and encryption
///     3. Key versioning and lifecycle management
///     4. Audit logging of key access
///     5. Geographic key isolation for compliance
///
///     Implementation Examples:
///     - Azure Key Vault: Uses Azure.Security.KeyVault.Keys SDK
///     - AWS KMS: Uses Amazon.KeyManagementService SDK
///     - Development: Uses in-memory keys with console logging
/// </summary>
public interface IKeyManagementService
{
    /// <summary>
    ///     Generates a new Data Encryption Key (DEK) for a specific customer/record.
    ///     The DEK is encrypted using the Master Key (KEK) specified by keyId.
    ///
    ///     Returns:
    ///     - Plaintext DEK (use immediately, do not persist)
    ///     - Encrypted DEK (persist with the data)
    /// </summary>
    Task<(byte[] PlaintextDek, byte[] EncryptedDek)> GenerateDataKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decrypts a Data Encryption Key (DEK) using the Master Key (KEK).
    ///
    ///     Security Note:
    ///     The plaintext DEK should only exist in memory and be disposed immediately after use.
    /// </summary>
    Task<byte[]> DecryptDataKeyAsync(
        string keyId,
        byte[] encryptedDek,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current (active) Master Key ID for encrypting new data.
    ///     This enables key rotation: new data uses the new key, old data keeps the old key.
    /// </summary>
    Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Validates that a key ID is still valid and not revoked.
    ///     Use this before decryption to fail fast if key is disabled.
    /// </summary>
    Task<bool> IsKeyValidAsync(string keyId, CancellationToken cancellationToken = default);
}

/// <summary>
///     2025 Elite Pattern: Encryption service with envelope encryption and blind indexing.
///
///     This service provides:
///     1. Transparent encryption/decryption using KMS
///     2. Envelope encryption (Master KEK + Data DEK)
///     3. Blind index computation for searchable encrypted fields
///     4. Deterministic vs Probabilistic encryption modes
///
///     Encryption Modes:
///     - Deterministic: Same plaintext → Same ciphertext (enables equality searches)
///     - Probabilistic: Same plaintext → Different ciphertext (prevents frequency analysis)
///
///     Use Cases:
///     - Deterministic: Phone numbers, email addresses (need exact match search)
///     - Probabilistic: Order notes, customer comments (no search needed, max security)
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    ///     Encrypts plaintext using envelope encryption.
    ///
    ///     Process:
    ///     1. Generate a unique Data Key (DEK) from KMS
    ///     2. Encrypt plaintext with DEK using AES-256-GCM
    ///     3. Encrypt DEK with Master Key (KEK)
    ///     4. Return ciphertext + encrypted DEK + metadata
    ///
    ///     Deterministic Mode:
    ///     If isDeterministic=true, derives DEK from plaintext hash (same input → same output).
    ///     Use ONLY for fields requiring exact match searches.
    /// </summary>
    Task<EncryptedData> EncryptAsync(
        string plaintext,
        bool isDeterministic = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decrypts encrypted data using envelope encryption.
    ///
    ///     Process:
    ///     1. Decrypt the DEK using Master Key from KMS
    ///     2. Decrypt ciphertext using the DEK
    ///     3. Dispose DEK from memory immediately
    ///
    ///     Security:
    ///     - Validates Key ID is still active
    ///     - Verifies AES-GCM authentication tag
    ///     - Logs access for audit trail
    /// </summary>
    Task<string> DecryptAsync(
        EncryptedData encryptedData,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Computes a blind index (HMAC-SHA256 hash) for searching encrypted fields.
    ///
    ///     The blind index enables database queries without decryption:
    ///     - One-way: Cannot derive plaintext from index
    ///     - Deterministic: Same input always produces same hash
    ///     - Salted: Prevents rainbow table attacks
    ///
    ///     Usage:
    ///     To search for phone="555-1234":
    ///     1. Compute: blindIndex = ComputeBlindIndex("555-1234")
    ///     2. Query: WHERE phone_blind_index = blindIndex
    /// </summary>
    Task<BlindIndex> ComputeBlindIndexAsync(
        string plaintext,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a complete SecureValue (encrypted data + blind index) in one operation.
    ///     This is the primary method for encrypting PII fields.
    ///
    ///     Example:
    ///     var securePhone = await encryptionService.CreateSecureValueAsync(
    ///         phoneNumber,
    ///         isDeterministic: true // Enable phone number searches
    ///     );
    ///
    ///     Database:
    ///     - encrypted_phone = securePhone.Encrypted.ToStorageFormat()
    ///     - phone_blind_index = securePhone.SearchIndex.Value
    /// </summary>
    Task<SecureValue> CreateSecureValueAsync(
        string plaintext,
        bool isDeterministic = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Re-encrypts data with a new key (key rotation).
    ///
    ///     Process:
    ///     1. Decrypt with old key
    ///     2. Encrypt with new key
    ///     3. Return new EncryptedData with updated KeyId
    ///
    ///     Use Case:
    ///     Periodic key rotation (NIST recommends annual rotation for high-value data).
    /// </summary>
    Task<EncryptedData> ReEncryptAsync(
        EncryptedData oldEncryptedData,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     2025 Elite Pattern: Configuration for blind index salt management.
///
///     The blind index salt MUST be:
///     1. Stored securely (Azure Key Vault, AWS Secrets Manager)
///     2. Different from encryption keys
///     3. Rotated annually
///     4. Never logged or exposed in error messages
///
///     Salt Rotation Strategy:
///     - Store multiple salts with version IDs
///     - New blind indexes use current salt
///     - Old blind indexes remain searchable with historical salts
///     - Background job re-hashes old indexes with new salt
/// </summary>
public interface IBlindIndexSaltProvider
{
    /// <summary>
    ///     Gets the current salt for computing new blind indexes.
    /// </summary>
    Task<byte[]> GetCurrentSaltAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a historical salt by version for searching old blind indexes.
    ///     Returns null if salt version is no longer available.
    /// </summary>
    Task<byte[]?> GetSaltByVersionAsync(
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current salt version for tracking which salt was used.
    /// </summary>
    Task<int> GetCurrentSaltVersionAsync(CancellationToken cancellationToken = default);
}
