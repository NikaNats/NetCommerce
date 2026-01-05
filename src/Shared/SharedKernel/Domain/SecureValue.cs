#region

using System.Security.Cryptography;
using System.Text;

#endregion

namespace NetCommerce.SharedKernel.Domain;

/// <summary>
///     2025 Elite Pattern: Blind Index for searching encrypted data.
///     Problem: If you encrypt a phone number, you can't query the database to find orders by phone.
///     Solution: Store a one-way HMAC-SHA256 hash (the "Blind Index") alongside the encrypted value.
///     The blind index allows O(1) database lookups without ever decrypting the entire table.
///     Security Properties:
///     - One-way: Cannot derive original value from blind index
///     - Deterministic: Same input always produces same hash (enables searching)
///     - Salted: Uses secret salt to prevent rainbow table attacks
///     Usage:
///     - Encrypt the value for display/storage: EncryptedData
///     - Hash the value for searching: BlindIndex
///     - Query: WHERE phone_blind_index = 'computed_hash'
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

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     2025 Elite Pattern: Encrypted data with envelope encryption metadata.
///     Envelope Encryption (NIST SP 800-57):
///     1. Master Key (KEK - Key Encryption Key): Stored in Azure Key Vault / AWS KMS
///     2. Data Key (DEK - Data Encryption Key): Unique per customer/record
///     3. The DEK is encrypted by the Master Key and stored with the data
///     Why Envelope Encryption?
///     - Performance: Decrypt DEK once, use it for all fields in the record
///     - Key Rotation: Re-encrypt only the small DEK, not all customer data
///     - Revocation: Delete a customer's DEK to instantly revoke access
///     - Compliance: Different customers can use different Master Keys (geographic boundaries)
///     Security Properties:
///     - AES-256-GCM: Authenticated encryption (prevents tampering)
///     - Unique IV per encryption: Prevents pattern analysis
///     - Key versioning: Supports key rotation without data migration
/// </summary>
public sealed record EncryptedData
{
    private EncryptedData(byte[] ciphertext, string keyId, byte[] iv, byte[]? encryptedDek)
    {
        Ciphertext = ciphertext;
        KeyId = keyId;
        Iv = iv;
        EncryptedDek = encryptedDek;
    }

    /// <summary>
    ///     The encrypted bytes (ciphertext) produced by AES-256-GCM.
    ///     Stored as Base64 string in database.
    /// </summary>
    public byte[] Ciphertext { get; init; }

    /// <summary>
    ///     The identifier of the Master Key (KEK) used to encrypt the DEK.
    ///     Format: "vault-key-id" or "arn:aws:kms:region:account:key/id"
    ///     This enables:
    ///     - Key rotation (multiple keys can coexist)
    ///     - Geographic compliance (EU data uses EU keys)
    ///     - Multi-tenancy (different customers use different keys)
    /// </summary>
    public string KeyId { get; init; }

    /// <summary>
    ///     Initialization Vector (IV) for AES-GCM.
    ///     Must be unique for every encryption operation.
    ///     CRITICAL: Never reuse an IV with the same key.
    ///     Reusing IVs breaks AES-GCM security guarantees.
    /// </summary>
    public byte[] Iv { get; init; }

    /// <summary>
    ///     Optional: The encrypted Data Encryption Key (DEK).
    ///     The DEK is encrypted using the Master Key (KEK) from KeyId.
    ///     This implements full envelope encryption:
    ///     - Master Key decrypts the DEK
    ///     - DEK decrypts the Ciphertext
    ///     If null, the KeyId directly encrypts the data (simpler but less flexible).
    /// </summary>
    public byte[]? EncryptedDek { get; init; }

    /// <summary>
    ///     Creates encrypted data with direct key encryption (no envelope).
    ///     Use for simple scenarios or when DEK management is external.
    /// </summary>
    public static EncryptedData Create(byte[] ciphertext, string keyId, byte[] iv)
    {
        if (ciphertext == null || ciphertext.Length == 0)
            throw new ArgumentException("Ciphertext cannot be empty.", nameof(ciphertext));

        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID is required.", nameof(keyId));

        if (iv == null || iv.Length == 0)
            throw new ArgumentException("IV is required.", nameof(iv));

        return new EncryptedData(ciphertext, keyId, iv, null);
    }

    /// <summary>
    ///     Creates encrypted data with full envelope encryption.
    ///     Use for maximum security and flexibility (recommended for 2025).
    /// </summary>
    public static EncryptedData CreateWithEnvelope(
        byte[] ciphertext,
        string keyId,
        byte[] iv,
        byte[] encryptedDek)
    {
        if (ciphertext == null || ciphertext.Length == 0)
            throw new ArgumentException("Ciphertext cannot be empty.", nameof(ciphertext));

        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID is required.", nameof(keyId));

        if (iv == null || iv.Length == 0)
            throw new ArgumentException("IV is required.", nameof(iv));

        if (encryptedDek == null || encryptedDek.Length == 0)
            throw new ArgumentException("Encrypted DEK is required for envelope encryption.", nameof(encryptedDek));

        return new EncryptedData(ciphertext, keyId, iv, encryptedDek);
    }

    /// <summary>
    ///     Converts encrypted data to a database-storable format.
    ///     Format: "KeyId|Base64(IV)|Base64(Ciphertext)|Base64(EncryptedDEK)"
    /// </summary>
    public string ToStorageFormat()
    {
        string[] parts = new[]
        {
            KeyId, Convert.ToBase64String(Iv), Convert.ToBase64String(Ciphertext),
            EncryptedDek != null ? Convert.ToBase64String(EncryptedDek) : string.Empty
        };

        return string.Join("|", parts);
    }

    /// <summary>
    ///     Parses encrypted data from database storage format.
    /// </summary>
    public static EncryptedData FromStorageFormat(string storageValue)
    {
        if (string.IsNullOrWhiteSpace(storageValue))
            throw new ArgumentException("Storage value cannot be empty.", nameof(storageValue));

        string[] parts = storageValue.Split('|');
        if (parts.Length < 3)
            throw new FormatException("Invalid encrypted data storage format.");

        string keyId = parts[0];
        byte[] iv = Convert.FromBase64String(parts[1]);
        byte[] ciphertext = Convert.FromBase64String(parts[2]);
        byte[]? encryptedDek = parts.Length > 3 && !string.IsNullOrEmpty(parts[3])
            ? Convert.FromBase64String(parts[3])
            : null;

        return encryptedDek != null
            ? CreateWithEnvelope(ciphertext, keyId, iv, encryptedDek)
            : Create(ciphertext, keyId, iv);
    }
}

/// <summary>
///     2025 Elite Pattern: Secure value combining encrypted data + blind index.
///     This is the complete package for PII fields:
///     - EncryptedData: For displaying the value to authorized users
///     - BlindIndex: For searching the database without decryption
///     Example:
///     Phone Number "555-1234" becomes:
///     - EncryptedData: AES-256-GCM encrypted bytes
///     - BlindIndex: HMAC-SHA256("555-1234" + salt) = "abc123..."
///     Database Schema:
///     - encrypted_phone: TEXT (stores EncryptedData.ToStorageFormat())
///     - phone_blind_index: TEXT (stores BlindIndex.Value)
///     - Index on phone_blind_index for fast lookups
///     Usage:
///     To search: WHERE phone_blind_index = BlindIndex.Compute(searchValue, salt)
///     To display: Decrypt EncryptedData using IEncryptionService
/// </summary>
public sealed record SecureValue
{
    private SecureValue(EncryptedData encrypted, BlindIndex searchIndex)
    {
        Encrypted = encrypted;
        SearchIndex = searchIndex;
    }

    public EncryptedData Encrypted { get; init; }
    public BlindIndex SearchIndex { get; init; }

    /// <summary>
    ///     Creates a secure value from plaintext.
    ///     This is used when encrypting a new value.
    /// </summary>
    public static SecureValue FromPlaintext(
        string plaintext,
        byte[] encryptionKey,
        string keyId,
        byte[] blindIndexSalt)
    {
        // Encrypt the value
        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.GenerateIV();

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        var encrypted = EncryptedData.Create(ciphertext, keyId, aes.IV);

        // Compute blind index
        var searchIndex = BlindIndex.Compute(plaintext, blindIndexSalt);

        return new SecureValue(encrypted, searchIndex);
    }

    /// <summary>
    ///     Creates a secure value from existing encrypted data and blind index.
    ///     This is used when loading from database.
    /// </summary>
    public static SecureValue FromStorage(EncryptedData encrypted, BlindIndex searchIndex)
    {
        return new SecureValue(encrypted, searchIndex);
    }

    /// <summary>
    ///     Decrypts the value to plaintext.
    ///     Requires the decryption key from KMS.
    /// </summary>
    public string Decrypt(byte[] decryptionKey)
    {
        using var aes = Aes.Create();
        aes.Key = decryptionKey;
        aes.IV = Encrypted.Iv;

        using ICryptoTransform decryptor = aes.CreateDecryptor();
        byte[] plaintextBytes = decryptor.TransformFinalBlock(Encrypted.Ciphertext, 0, Encrypted.Ciphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
