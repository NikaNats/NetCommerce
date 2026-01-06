#nullable enable
using NetCommerce.Kernel.Core.Encryption; // Dependency on Core

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
/// The "How" - Business logic for performing encryption.
/// Implementation will reside in Infrastructure/Adapters.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Returns the consolidated EncryptedData model from Core.
    /// </summary>
    Task<EncryptedData> EncryptAsync(string plaintext, bool isDeterministic = false, CancellationToken cancellationToken = default);

    Task<string> DecryptAsync(EncryptedData encryptedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous versions for EF Core Value Converters.
    /// </summary>
    EncryptedData Encrypt(string plaintext, bool isDeterministic = false);
    string Decrypt(EncryptedData encryptedData);

    BlindIndex ComputeBlindIndex(string plaintext);
}
