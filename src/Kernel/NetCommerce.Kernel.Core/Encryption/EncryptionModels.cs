#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace NetCommerce.Kernel.Core.Encryption;

/// <summary>
/// Blind Index for searchable encrypted data.
/// Defined in Core because it's a data contract that travels through all layers.
/// </summary>
public sealed record BlindIndex(string Value)
{
    public static BlindIndex FromHash(string hashValue) => new(hashValue);

    public static BlindIndex Compute(string plaintext, byte[] salt)
    {
        using var hmac = new HMACSHA256(salt);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plaintext));
        return new BlindIndex(Convert.ToBase64String(hash));
    }

    public override string ToString() => Value;
}

/// <summary>
/// Immutable Encryption Metadata Container.
/// This is the "What" - what was encrypted and how it's stored.
/// </summary>
public sealed record EncryptedData(
    byte[] Ciphertext,
    string KeyId,
    byte[] Iv,
    byte[]? EncryptedDek = null,
    int Version = 1,
    string AlgorithmType = "AES-256-GCM",
    int AlgorithmVersion = 1)
{
    public string ToStorageFormat()
    {
        var ivBase64 = Convert.ToBase64String(Iv);
        var ciphertextBase64 = Convert.ToBase64String(Ciphertext);
        var dekBase64 = EncryptedDek != null ? Convert.ToBase64String(EncryptedDek) : string.Empty;

        return $"v{Version}|a{AlgorithmType}:{AlgorithmVersion}|k{KeyId}|{ivBase64}|{ciphertextBase64}|{dekBase64}";
    }

    public static EncryptedData FromStorageFormat(string storageFormat)
    {
        var parts = storageFormat.Split('|');
        if (parts.Length < 5) throw new FormatException("Invalid encrypted storage format.");

        var version = 1;
        var algorithmType = "AES-256-GCM";
        var algorithmVersion = 1;

        // Parse version (first part, format: v{version})
        if (parts[0].StartsWith('v'))
        {
            version = int.Parse(parts[0][1..]);
        }

        // Parse algorithm metadata (second part, format: a{algorithmType}:{algorithmVersion})
        if (parts.Length >= 6 && parts[1].StartsWith('a'))
        {
            var algorithmPart = parts[1][1..];
            var algorithmParts = algorithmPart.Split(':');
            if (algorithmParts.Length == 2)
            {
                algorithmType = algorithmParts[0];
                algorithmVersion = int.Parse(algorithmParts[1]);
            }
        }

        // Parse key ID (third part, format: k{keyId})
        if (!parts[2].StartsWith('k'))
        {
            throw new FormatException("Invalid key ID format");
        }
        var keyId = parts[2][1..];

        // Parse IV, ciphertext, and optional DEK
        var iv = Convert.FromBase64String(parts[3]);
        var ciphertext = Convert.FromBase64String(parts[4]);
        var encryptedDek = parts.Length >= 6 && !string.IsNullOrEmpty(parts[5])
            ? Convert.FromBase64String(parts[5])
            : null;

        return new EncryptedData(ciphertext, keyId, iv, encryptedDek, version, algorithmType, algorithmVersion);
    }
}
