#nullable enable
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetCommerce.Kernel.Compliance.Encryption;

namespace NetCommerce.Kernel.EfCore.Converters;

/// <summary>
///     EF Core Value Converter for transparent PII encryption.
///     Business logic works with plaintext, database stores ciphertext.
/// </summary>
public class PiiEncryptionConverter : ValueConverter<string, string>
{
    public PiiEncryptionConverter(IEncryptionService encryptionService)
        : this(encryptionService, isDeterministic: true)
    {
    }

    public PiiEncryptionConverter(IEncryptionService encryptionService, bool isDeterministic)
        : base(
            plaintext => EncryptForStorage(plaintext, encryptionService, isDeterministic),
            encrypted => DecryptFromStorage(encrypted, encryptionService)
        )
    {
    }

    private static string EncryptForStorage(
        string plaintext,
        IEncryptionService encryptionService,
        bool isDeterministic)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            return string.Empty;

        var encryptedData = encryptionService.Encrypt(plaintext, isDeterministic);

        return encryptedData.ToStorageFormat();
    }

    private static string DecryptFromStorage(
        string encrypted,
        IEncryptionService encryptionService)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return string.Empty;

        var encryptedData = EncryptedData.FromStorageFormat(encrypted);

        var plaintext = encryptionService.Decrypt(encryptedData);

        return plaintext;
    }
}

/// <summary>
///     EF Core Value Converter for blind indexes.
///     Stores HMAC-SHA256 hash for searchable encrypted fields.
/// </summary>
public class BlindIndexConverter : ValueConverter<BlindIndex, string>
{
    public BlindIndexConverter()
        : base(
            blindIndex => blindIndex.Value,
            hashValue => BlindIndex.FromHash(hashValue)
        )
    {
    }
}
