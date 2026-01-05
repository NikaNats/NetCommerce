#region

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;

#endregion

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Converters;

/// <summary>
///     2025 Elite Pattern: EF Core Value Converter for transparent PII encryption.
///     This converter automatically encrypts/decrypts PII fields when saving/loading from database.
///     Business logic works with plaintext, database stores ciphertext.
///     Benefits:
///     - Zero coupling: Domain models never call IEncryptionService
///     - Transparent: Developers don't need to remember to encrypt
///     - Centralized: Change encryption in one place
///     - Type-safe: Compiler enforces encryption on configured properties
///     Usage in Entity Configuration:
///     builder.Property(o => o.ShippingAddress.PhoneNumber)
///     .HasConversion(new PiiEncryptionConverter(encryptionService))
///     .HasColumnName("encrypted_phone");
///     Database Schema:
///     encrypted_phone TEXT NOT NULL -- Stores: "KeyId|IV|Ciphertext|EncryptedDEK"
///     Security Note:
///     This converter uses deterministic encryption by default to enable exact match searches.
///     For fields that don't need searching (comments, notes), use isDeterministic: false.
/// </summary>
public class PiiEncryptionConverter : ValueConverter<string, string>
{
    /// <summary>
    ///     Creates a PII encryption converter with deterministic encryption (enables searches).
    /// </summary>
    public PiiEncryptionConverter(IEncryptionService encryptionService)
        : this(encryptionService, true)
    {
    }

    /// <summary>
    ///     Creates a PII encryption converter with configurable encryption mode.
    ///     Deterministic Mode (isDeterministic=true):
    ///     - Same plaintext → Same ciphertext
    ///     - Enables equality searches (WHERE phone = '555-1234')
    ///     - Use for: Phone numbers, email addresses, SSN
    ///     Probabilistic Mode (isDeterministic=false):
    ///     - Same plaintext → Different ciphertext each time
    ///     - Prevents frequency analysis attacks
    ///     - Use for: Comments, notes, free-text fields
    /// </summary>
    public PiiEncryptionConverter(IEncryptionService encryptionService, bool isDeterministic)
        : base(
            // To Database: Encrypt plaintext → storage format
            plaintext => EncryptForStorage(plaintext, encryptionService, isDeterministic),
            // From Database: Decrypt storage format → plaintext
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

        // Encrypt synchronously for EF Core compatibility
        // In production, consider caching DEKs to avoid KMS roundtrips
        EncryptedData encryptedData = encryptionService.EncryptAsync(plaintext, isDeterministic)
            .GetAwaiter()
            .GetResult();

        return encryptedData.ToStorageFormat();
    }

    private static string DecryptFromStorage(
        string encrypted,
        IEncryptionService encryptionService)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return string.Empty;

        var encryptedData = EncryptedData.FromStorageFormat(encrypted);

        // Decrypt synchronously for EF Core compatibility
        string plaintext = encryptionService.DecryptAsync(encryptedData)
            .GetAwaiter()
            .GetResult();

        return plaintext;
    }
}

/// <summary>
///     2025 Elite Pattern: EF Core Value Converter for blind indexes.
///     This converter stores a HMAC-SHA256 hash of the encrypted field for searching.
///     The blind index enables O(1) database lookups without decrypting the entire table:
///     - Query: WHERE phone_blind_index = BlindIndex.Compute("555-1234", salt)
///     - Returns matching records instantly
///     - Never exposes plaintext
///     Usage in Entity Configuration:
///     builder.Property
///     <string>
///         ("PhoneBlindIndex")
///         .HasConversion(new BlindIndexConverter(encryptionService))
///         .HasColumnName("phone_blind_index")
///         .IsRequired();
///         builder.HasIndex("PhoneBlindIndex")
///         .HasDatabaseName("ix_orders_phone_blind_index");
///         Database Schema:
///         phone_blind_index TEXT NOT NULL -- Stores: Base64(HMAC-SHA256(phone + salt))
///         INDEX ix_orders_phone_blind_index (phone_blind_index)
///         Security Properties:
///         - One-way: Cannot derive phone number from blind index
///         - Deterministic: Same phone → Same hash (enables searching)
///         - Salted: Prevents rainbow table attacks
/// </summary>
public class BlindIndexConverter : ValueConverter<string, string>
{
    public BlindIndexConverter(IEncryptionService encryptionService)
        : base(
            // To Database: Compute blind index from plaintext
            plaintext => ComputeBlindIndex(plaintext, encryptionService),
            // From Database: Blind index is already a hash, no conversion needed
            blindIndex => blindIndex
        )
    {
    }

    private static string ComputeBlindIndex(
        string plaintext,
        IEncryptionService encryptionService)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            return string.Empty;

        // Compute blind index synchronously for EF Core compatibility
        BlindIndex blindIndex = encryptionService.ComputeBlindIndexAsync(plaintext)
            .GetAwaiter()
            .GetResult();

        return blindIndex.Value;
    }
}

/// <summary>
///     2025 Elite Pattern: EF Core Value Converter for complete SecureValue.
///     This converter handles both encryption AND blind index in a single property.
///     Use this when you want a cleaner domain model without separate blind index fields.
///     Example Domain Model:
///     public class Order
///     {
///     public SecureValue PhoneNumber { get; private set; }
///     }
///     Entity Configuration:
///     builder.OwnsOne(o => o.PhoneNumber, phoneBuilder =>
///     {
///     phoneBuilder.Property(p => p.Encrypted)
///     .HasConversion
///     <EncryptedDataConverter>
///         ()
///         .HasColumnName("encrypted_phone");
///         phoneBuilder.Property(p => p.SearchIndex)
///         .HasConversion
///         <BlindIndexValueConverter>
///             ()
///             .HasColumnName("phone_blind_index");
///             phoneBuilder.HasIndex(p => p.SearchIndex)
///             .HasDatabaseName("ix_orders_phone_blind_index");
///             });
///             This approach keeps domain models clean while maintaining searchability.
/// </summary>
public class EncryptedDataConverter : ValueConverter<EncryptedData, string>
{
    public EncryptedDataConverter()
        : base(
            encryptedData => encryptedData.ToStorageFormat(),
            storageValue => EncryptedData.FromStorageFormat(storageValue)
        )
    {
    }
}

/// <summary>
///     EF Core Value Converter for BlindIndex type.
/// </summary>
public class BlindIndexValueConverter : ValueConverter<BlindIndex, string>
{
    public BlindIndexValueConverter()
        : base(
            blindIndex => blindIndex.Value,
            hashValue => BlindIndex.FromHash(hashValue)
        )
    {
    }
}
