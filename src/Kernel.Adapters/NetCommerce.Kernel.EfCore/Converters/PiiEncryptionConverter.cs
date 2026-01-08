#nullable enable
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetCommerce.Kernel.Compliance.Encryption;
using NetCommerce.Kernel.Core.Encryption;

namespace NetCommerce.Kernel.EfCore.Converters;

/// <summary>
///     2025 Elite Pattern: EF Core Value Converter for transparent PII encryption.
///     Automatically encrypts/decrypts PII fields when saving/loading using Cached DEKs.
/// </summary>
public class PiiEncryptionConverter : ValueConverter<string, string>
{
    public PiiEncryptionConverter(ICryptoProvider cryptoProvider, bool isDeterministic = true)
        : base(
            // To Database: Encrypt plaintext -> storage format
            plaintext => EncryptForStorage(plaintext, cryptoProvider, isDeterministic),
            // From Database: Decrypt storage format -> plaintext
            encrypted => DecryptFromStorage(encrypted, cryptoProvider),
            // Hints
            new ConverterMappingHints(size: null, unicode: false)
        )
    {
    }

    private static string EncryptForStorage(string plaintext, ICryptoProvider provider, bool isDeterministic)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;

        // High-perf: Span-based encryption using cached keys
        var result = provider.Encrypt(plaintext.AsSpan(), isDeterministic);
        return result.ToStorageFormat();
    }

    private static string DecryptFromStorage(string encrypted, ICryptoProvider provider)
    {
        if (string.IsNullOrEmpty(encrypted)) return string.Empty;

        // Backward compatibility check
        if (!encrypted.StartsWith("v")) return encrypted;

        // FIX: Use Core namespace, not Compliance
        var data = NetCommerce.Kernel.Core.Encryption.EncryptedData.FromStorageFormat(encrypted);
        return provider.Decrypt(data);
    }
}

/// <summary>
///     EF Core Value Converter for BlindIndex type.
///     Maps the Domain Value Object (BlindIndex) to the Database Column (Hash String).
/// </summary>
public class BlindIndexValueConverter : ValueConverter<NetCommerce.Kernel.Core.Encryption.BlindIndex, string>
{
    public BlindIndexValueConverter()
        : base(
            // Domain -> DB: Just extract the hash string
            blindIndex => blindIndex.Value,
            // DB -> Domain: Wrap the hash string
            hashValue => NetCommerce.Kernel.Core.Encryption.BlindIndex.FromHash(hashValue)
        )
    {
    }
}

/// <summary>
///     EF Core Value Converter for complete EncryptedData objects.
///     Used when the domain model contains the raw encryption metadata.
/// </summary>
public class EncryptedDataConverter : ValueConverter<NetCommerce.Kernel.Core.Encryption.EncryptedData, string>
{
    public EncryptedDataConverter()
        : base(
            data => data.ToStorageFormat(),
            storageValue => NetCommerce.Kernel.Core.Encryption.EncryptedData.FromStorageFormat(storageValue)
        )
    {
    }
}
