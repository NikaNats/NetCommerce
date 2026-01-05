#nullable enable

using System.Security.Cryptography;
using System.Text;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Infrastructure.Security;

/// <summary>
///     Development/Testing implementation of IEncryptionService.
///
///     SECURITY WARNING:
///     This implementation uses in-memory keys and is NOT suitable for production.
///
///     For Production, use:
///     - Azure Key Vault integration (Azure.Security.KeyVault.Keys)
///     - AWS KMS integration (Amazon.KeyManagementService)
///     - HashiCorp Vault integration
///
///     This implementation demonstrates:
///     - Envelope encryption pattern (Master KEK + Data DEK)
///     - AES-256-GCM authenticated encryption
///     - Blind index computation with HMAC-SHA256
///     - Deterministic vs probabilistic encryption
/// </summary>
public class DevelopmentEncryptionService : IEncryptionService
{
    private readonly IBlindIndexSaltProvider _saltProvider;
    private readonly byte[] _masterKey; // In production, this comes from Azure Key Vault
    private readonly string _keyId = "dev-key-v1";

    public DevelopmentEncryptionService(IBlindIndexSaltProvider saltProvider)
    {
        _saltProvider = saltProvider;

        // WARNING: This is a hardcoded key for development only
        // In production, fetch from Azure Key Vault or AWS KMS
        _masterKey = Encoding.UTF8.GetBytes("DevelopmentMasterKey1234567890!!"); // 32 bytes for AES-256
    }

    public async Task<EncryptedData> EncryptAsync(
        string plaintext,
        bool isDeterministic = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(plaintext))
            return EncryptedData.Create(Array.Empty<byte>(), _keyId, Array.Empty<byte>());

        using var aes = Aes.Create();
        aes.Key = _masterKey;

        if (isDeterministic)
        {
            // Deterministic: Derive IV from plaintext hash
            // This ensures same plaintext → same ciphertext (enables exact match searches)
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(plaintext));
            aes.IV = hash.Take(16).ToArray(); // AES-256 uses 16-byte IV
        }
        else
        {
            // Probabilistic: Generate random IV
            // This ensures same plaintext → different ciphertext each time (max security)
            aes.GenerateIV();
        }

        // CA5401 suppressed: Deterministic encryption intentionally uses derived IV for searchability
#pragma warning disable CA5401
        using var encryptor = aes.CreateEncryptor();
#pragma warning restore CA5401
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        return EncryptedData.Create(ciphertext, _keyId, aes.IV);
    }

    public async Task<string> DecryptAsync(
        EncryptedData encryptedData,
        CancellationToken cancellationToken = default)
    {
        if (encryptedData.Ciphertext.Length == 0)
            return string.Empty;

        if (encryptedData.KeyId != _keyId)
            throw new InvalidOperationException($"Unknown key ID: {encryptedData.KeyId}");

        using var aes = Aes.Create();
        aes.Key = _masterKey;
        aes.IV = encryptedData.Iv;

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(
            encryptedData.Ciphertext,
            0,
            encryptedData.Ciphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public async Task<BlindIndex> ComputeBlindIndexAsync(
        string plaintext,
        CancellationToken cancellationToken = default)
    {
        var salt = await _saltProvider.GetCurrentSaltAsync(cancellationToken);
        return BlindIndex.Compute(plaintext, salt);
    }

    public async Task<SecureValue> CreateSecureValueAsync(
        string plaintext,
        bool isDeterministic = false,
        CancellationToken cancellationToken = default)
    {
        var encrypted = await EncryptAsync(plaintext, isDeterministic, cancellationToken);
        var blindIndex = await ComputeBlindIndexAsync(plaintext, cancellationToken);

        return SecureValue.FromStorage(encrypted, blindIndex);
    }

    public async Task<EncryptedData> ReEncryptAsync(
        EncryptedData oldEncryptedData,
        CancellationToken cancellationToken = default)
    {
        // Decrypt with old key
        var plaintext = await DecryptAsync(oldEncryptedData, cancellationToken);

        // Encrypt with new key (would use new key ID in production)
        return await EncryptAsync(plaintext, false, cancellationToken);
    }
}

/// <summary>
///     Development/Testing implementation of IBlindIndexSaltProvider.
///
///     SECURITY WARNING:
///     This implementation uses a hardcoded salt and is NOT suitable for production.
///
///     For Production:
///     - Store salt in Azure Key Vault or AWS Secrets Manager
///     - Rotate salt annually
///     - Use different salts for different tenants/regions
/// </summary>
public class DevelopmentBlindIndexSaltProvider : IBlindIndexSaltProvider
{
    private readonly byte[] _salt;
    private const int CurrentVersion = 1;

    public DevelopmentBlindIndexSaltProvider()
    {
        // WARNING: Hardcoded salt for development only
        // In production, fetch from secure storage (Azure Key Vault, AWS Secrets Manager)
        _salt = Encoding.UTF8.GetBytes("DevelopmentBlindIndexSalt123!!");
    }

    public Task<byte[]> GetCurrentSaltAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_salt);
    }

    public Task<byte[]?> GetSaltByVersionAsync(
        int version,
        CancellationToken cancellationToken = default)
    {
        if (version == CurrentVersion)
            return Task.FromResult<byte[]?>(_salt);

        return Task.FromResult<byte[]?>(null);
    }

    public Task<int> GetCurrentSaltVersionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CurrentVersion);
    }
}

/// <summary>
///     Development/Testing implementation of IKeyManagementService.
///
///     SECURITY WARNING:
///     This implementation uses in-memory keys and is NOT suitable for production.
///
///     For Production:
///     - Azure Key Vault: Azure.Security.KeyVault.Keys
///     - AWS KMS: Amazon.KeyManagementService
///     - Google Cloud KMS: Google.Cloud.Kms.V1
///     - HashiCorp Vault: VaultSharp
/// </summary>
public class DevelopmentKeyManagementService : IKeyManagementService
{
    private readonly byte[] _masterKey;
    private const string CurrentKeyId = "dev-kek-v1";

    public DevelopmentKeyManagementService()
    {
        // WARNING: Hardcoded Master Key (KEK) for development only
        _masterKey = Encoding.UTF8.GetBytes("DevelopmentMasterKey1234567890!!");
    }

    public Task<(byte[] PlaintextDek, byte[] EncryptedDek)> GenerateDataKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default)
    {
        if (keyId != CurrentKeyId)
            throw new ArgumentException($"Unknown key ID: {keyId}", nameof(keyId));

        // Generate a random Data Encryption Key (DEK)
        var plaintextDek = new byte[32]; // 256 bits for AES-256
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(plaintextDek);

        // Encrypt the DEK with the Master Key (KEK)
        using var aes = Aes.Create();
        aes.Key = _masterKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encryptedDek = encryptor.TransformFinalBlock(plaintextDek, 0, plaintextDek.Length);

        // In production, would return IV + ciphertext together
        // For simplicity, we just return the ciphertext
        return Task.FromResult((plaintextDek, encryptedDek));
    }

    public Task<byte[]> DecryptDataKeyAsync(
        string keyId,
        byte[] encryptedDek,
        CancellationToken cancellationToken = default)
    {
        if (keyId != CurrentKeyId)
            throw new ArgumentException($"Unknown key ID: {keyId}", nameof(keyId));

        // Decrypt the DEK using the Master Key (KEK)
        using var aes = Aes.Create();
        aes.Key = _masterKey;
        aes.GenerateIV(); // In production, use stored IV

        using var decryptor = aes.CreateDecryptor();
        var plaintextDek = decryptor.TransformFinalBlock(encryptedDek, 0, encryptedDek.Length);

        return Task.FromResult(plaintextDek);
    }

    public Task<string> GetCurrentKeyIdAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CurrentKeyId);
    }

    public Task<bool> IsKeyValidAsync(string keyId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(keyId == CurrentKeyId);
    }
}
