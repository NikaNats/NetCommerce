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
    ///     Decrypts the Data Encryption Key (DEK) using the Cloud Master Key.
    ///     This is the SLOW HTTP call.
    /// </summary>
    Task<byte[]> UnwrapKeyAsync(string encryptedDekBase64, CancellationToken ct = default);

    /// <summary>
    ///     Gets the currently active Encrypted DEK (usually from config or DB).
    /// </summary>
    Task<string> GetActiveEncryptedDekAsync(CancellationToken ct = default);
}

/// <summary>
/// The "How" - Business logic for performing encryption.
/// Implementation will reside in Infrastructure/Adapters.
/// </summary>
public interface ICryptoProvider
{
    // Fast, synchronous, allocation-free (where possible)
    NetCommerce.Kernel.Core.Encryption.EncryptedData Encrypt(ReadOnlySpan<char> plaintext, bool isDeterministic = false);
    string Decrypt(NetCommerce.Kernel.Core.Encryption.EncryptedData data);

    /// <summary>
    ///     Computes the Blind Index (HMAC-SHA256) for searching.
    ///     Must be deterministic (Same Input + Same Salt = Same Output).
    /// </summary>
    NetCommerce.Kernel.Core.Encryption.BlindIndex ComputeBlindIndex(ReadOnlySpan<char> plaintext);
}

/// <summary>
/// Legacy interface - kept for backward compatibility.
/// New implementations should use ICryptoProvider for performance.
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
