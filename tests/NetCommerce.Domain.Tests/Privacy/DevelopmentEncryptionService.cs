#nullable enable
using System.Security.Cryptography;
using System.Text;
using NetCommerce.Kernel.Compliance.Encryption;
using NetCommerce.Kernel.Core.Encryption;

namespace NetCommerce.Domain.Tests.Privacy;

/// <summary>
///     Development/Testing implementation of IEncryptionService.
///     SECURITY WARNING:
///     This implementation uses in-memory keys and is NOT suitable for production.
/// </summary>
public class DevelopmentEncryptionService : IEncryptionService
{
    private readonly string _keyId = "dev-key-v1";
    private readonly byte[] _masterKey; // In production, this comes from Azure Key Vault
    private readonly IBlindIndexSaltProvider _saltProvider;

    public DevelopmentEncryptionService(IBlindIndexSaltProvider saltProvider)
    {
        _saltProvider = saltProvider;
        // WARNING: This is a hardcoded key for development only
        _masterKey = Encoding.UTF8.GetBytes("DevelopmentMasterKey1234567890!!"); // 32 bytes for AES-256
    }

    public async Task<EncryptedData> EncryptAsync(string plaintext, bool isDeterministic = false, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(Encrypt(plaintext, isDeterministic));
    }

    public async Task<string> DecryptAsync(EncryptedData encryptedData, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(Decrypt(encryptedData));
    }

    public EncryptedData Encrypt(string plaintext, bool isDeterministic = false)
    {
        if (string.IsNullOrEmpty(plaintext))
            return new EncryptedData(Array.Empty<byte>(), _keyId, Array.Empty<byte>());

        using var aes = Aes.Create();
        aes.Key = _masterKey;
        aes.GenerateIV();

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        return new EncryptedData(ciphertext, _keyId, aes.IV);
    }

    public string Decrypt(EncryptedData encryptedData)
    {
        if (encryptedData.Ciphertext.Length == 0)
            return string.Empty;

        if (encryptedData.KeyId != _keyId)
            throw new InvalidOperationException($"Unknown key ID: {encryptedData.KeyId}");

        using var aes = Aes.Create();
        aes.Key = _masterKey;
        aes.IV = encryptedData.Iv;

        using ICryptoTransform decryptor = aes.CreateDecryptor();
        byte[] plaintextBytes = decryptor.TransformFinalBlock(
            encryptedData.Ciphertext,
            0,
            encryptedData.Ciphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public BlindIndex ComputeBlindIndex(string plaintext)
    {
        byte[] salt = _saltProvider.GetCurrentSaltAsync().Result;
        return BlindIndex.Compute(plaintext, salt);
    }
}
