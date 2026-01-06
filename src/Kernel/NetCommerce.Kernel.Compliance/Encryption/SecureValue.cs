#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace NetCommerce.Kernel.Compliance.Encryption;

/// <summary>
///     Blind Index for searching encrypted data.
///     Stores a one-way HMAC-SHA256 hash for O(1) database lookups without decryption.
/// </summary>
public sealed record BlindIndex
{
    private BlindIndex(string value)
    {
        Value = value;
    }

    /// <summary>
    ///     The HMAC-SHA256 hash of the original value + secret salt.
    ///     Stored as Base64 string for database compatibility.
    /// </summary>
    public string Value { get; init; }

    /// <summary>
    ///     Creates a blind index from a pre-computed hash value.
    ///     Use this when loading from database.
    /// </summary>
    public static BlindIndex FromHash(string hashValue)
    {
        if (string.IsNullOrWhiteSpace(hashValue))
            throw new ArgumentException("Blind index hash cannot be empty.", nameof(hashValue));

        return new BlindIndex(hashValue);
    }

    /// <summary>
    ///     Computes a blind index from plaintext value using HMAC-SHA256.
    ///     Use this when creating a new searchable encrypted field.
    /// </summary>
    public static BlindIndex Compute(string plaintext, byte[] salt)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Plaintext cannot be empty.", nameof(plaintext));

        if (salt == null || salt.Length == 0)
            throw new ArgumentException("Salt is required for blind index.", nameof(salt));

        using var hmac = new HMACSHA256(salt);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plaintext));
        string hashString = Convert.ToBase64String(hash);

        return new BlindIndex(hashString);
    }

    public override string ToString() => Value;
}

/// <summary>
///     Encrypted data with envelope encryption metadata.
///     Envelope Encryption (NIST SP 800-57):
///     1. Master Key (KEK): Stored in Key Vault / KMS
///     2. Data Key (DEK): Unique per customer/record
///     3. The DEK is encrypted by the Master Key and stored with the data
/// </summary>
public sealed record EncryptedData
{
    private EncryptedData(byte[] ciphertext, string keyId, byte[] iv, byte[]? encryptedDek, int version = 1, string algorithmType = "AES-256-GCM", int algorithmVersion = 1)
    {
        Ciphertext = ciphertext;
        KeyId = keyId;
        Iv = iv;
        EncryptedDek = encryptedDek;
        Version = version;
        AlgorithmType = algorithmType;
        AlgorithmVersion = algorithmVersion;
    }

    /// <summary>
    ///     The encrypted data (AES-256-GCM ciphertext).
    /// </summary>
    public byte[] Ciphertext { get; init; }

    /// <summary>
    ///     The Master Key ID used to encrypt the DEK.
    ///     Enables key rotation without re-encrypting all data.
    /// </summary>
    public string KeyId { get; init; }

    /// <summary>
    ///     Initialization Vector for AES decryption.
    ///     Unique per encryption operation.
    /// </summary>
    public byte[] Iv { get; init; }

    /// <summary>
    ///     The Data Encryption Key encrypted by the Master Key.
    ///     Null for deterministic encryption (uses derived key).
    /// </summary>
    public byte[]? EncryptedDek { get; init; }

    /// <summary>
    ///     Version of the encryption format.
    ///     Enables future encryption algorithm upgrades.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    ///     The encryption algorithm type (e.g., "AES-256-GCM", "ChaCha20-Poly1305").
    ///     Enables future algorithm migrations.
    /// </summary>
    public string AlgorithmType { get; init; }

    /// <summary>
    ///     Version of the encryption algorithm.
    ///     Enables algorithm upgrades within the same type.
    /// </summary>
    public int AlgorithmVersion { get; init; }

    /// <summary>
    ///     Creates an EncryptedData instance.
    /// </summary>
    public static EncryptedData Create(byte[] ciphertext, string keyId, byte[] iv, byte[]? encryptedDek = null, int version = 1, string algorithmType = "AES-256-GCM", int algorithmVersion = 1)
    {
        return new EncryptedData(ciphertext, keyId, iv, encryptedDek, version, algorithmType, algorithmVersion);
    }

    /// <summary>
    ///     Serializes to a storage-friendly format: "v{Version}|a{AlgorithmType}:{AlgorithmVersion}|KeyId|IV|Ciphertext|EncryptedDEK"
    /// </summary>
    public string ToStorageFormat()
    {
        var ivBase64 = Convert.ToBase64String(Iv);
        var ciphertextBase64 = Convert.ToBase64String(Ciphertext);
        var dekBase64 = EncryptedDek != null ? Convert.ToBase64String(EncryptedDek) : string.Empty;

        return $"v{Version}|a{AlgorithmType}:{AlgorithmVersion}|{KeyId}|{ivBase64}|{ciphertextBase64}|{dekBase64}";
    }

    /// <summary>
    ///     Deserializes from storage format.
    /// </summary>
    public static EncryptedData FromStorageFormat(string storageFormat)
    {
        var parts = storageFormat.Split('|');
        if (parts.Length < 5)
            throw new ArgumentException("Invalid encrypted data format.", nameof(storageFormat));

        int version = 1; // Default for backward compatibility
        string algorithmType = "AES-256-GCM"; // Default
        int algorithmVersion = 1; // Default
        int keyIdIndex = 0;

        // Parse version
        if (parts[0].StartsWith("v"))
        {
            if (!int.TryParse(parts[0].AsSpan(1), out version))
                throw new ArgumentException("Invalid version format.", nameof(storageFormat));
            keyIdIndex = 1;
        }

        // Parse algorithm info
        if (parts.Length > keyIdIndex && parts[keyIdIndex].StartsWith("a"))
        {
            var algorithmPart = parts[keyIdIndex];
            var colonIndex = algorithmPart.IndexOf(':');
            if (colonIndex > 1)
            {
                algorithmType = algorithmPart.Substring(1, colonIndex - 1);
                if (!int.TryParse(algorithmPart.AsSpan(colonIndex + 1), out algorithmVersion))
                    throw new ArgumentException("Invalid algorithm version format.", nameof(storageFormat));
                keyIdIndex++;
            }
        }

        var keyId = parts[keyIdIndex];
        var iv = Convert.FromBase64String(parts[keyIdIndex + 1]);
        var ciphertext = Convert.FromBase64String(parts[keyIdIndex + 2]);
        var encryptedDek = parts.Length > keyIdIndex + 3 && !string.IsNullOrEmpty(parts[keyIdIndex + 3])
            ? Convert.FromBase64String(parts[keyIdIndex + 3])
            : null;

        return new EncryptedData(ciphertext, keyId, iv, encryptedDek, version, algorithmType, algorithmVersion);
    }
}
