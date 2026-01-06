#nullable enable
namespace NetCommerce.Kernel.Compliance.Encryption;

/// <summary>
///     Key Management Service abstraction.
///     Abstracts Azure Key Vault, AWS KMS, HashiCorp Vault, or custom HSM.
/// </summary>
public interface IKeyManagementService
{
    /// <summary>
    ///     Generates a new Data Encryption Key (DEK) for a specific customer/record.
    ///     Returns: Plaintext DEK (use immediately), Encrypted DEK (persist with data)
    /// </summary>
    Task<(byte[] PlaintextDek, byte[] EncryptedDek)> GenerateDataKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decrypts a Data Encryption Key (DEK) using the Master Key (KEK).
    /// </summary>
    Task<byte[]> DecryptDataKeyAsync(
        string keyId,
        byte[] encryptedDek,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current (active) Master Key ID for encrypting new data.
    /// </summary>
    Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Validates that a key ID exists and is active.
    /// </summary>
    Task<bool> ValidateKeyIdAsync(string keyId, CancellationToken cancellationToken = default);
}

/// <summary>
///     Encryption service for PII and sensitive data.
///     Provides both deterministic (searchable) and probabilistic (max security) encryption.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    ///     Encrypts plaintext data.
    /// </summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <param name="isDeterministic">
    ///     True: Same plaintext → Same ciphertext (enables equality searches)
    ///     False: Same plaintext → Different ciphertext (prevents frequency analysis)
    /// </param>
    Task<EncryptedData> EncryptAsync(string plaintext, bool isDeterministic = false, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decrypts encrypted data back to plaintext.
    /// </summary>
    Task<string> DecryptAsync(EncryptedData encryptedData, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Encrypts plaintext data synchronously (for EF Core value converters).
    ///     ⚠️ Use only in EF Core converters where async is not possible.
    /// </summary>
    EncryptedData Encrypt(string plaintext, bool isDeterministic = false);

    /// <summary>
    ///     Decrypts encrypted data synchronously (for EF Core value converters).
    ///     ⚠️ Use only in EF Core converters where async is not possible.
    /// </summary>
    string Decrypt(EncryptedData encryptedData);

    /// <summary>
    ///     Computes a blind index for searchable encrypted fields.
    /// </summary>
    BlindIndex ComputeBlindIndex(string plaintext);
}
