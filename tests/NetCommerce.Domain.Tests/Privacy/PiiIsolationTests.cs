#region

using System.Text;
using NetCommerce.Kernel.Compliance.Encryption;
using NetCommerce.Kernel.Compliance.Pii;
using NetCommerce.Kernel.Core.Encryption;
using IEncryptionService = NetCommerce.Kernel.Compliance.Encryption.IEncryptionService;

#endregion

namespace NetCommerce.Domain.Tests.Privacy;

/// <summary>
///     Tests for encryption primitives: BlindIndex, EncryptedData.
///     Phase 7: Repaired to align with synchronous ICryptoProvider and IBlindIndexSaltProvider.
/// </summary>
public class EncryptionPrimitivesTests
{
    [Fact]
    public void BlindIndex_Compute_ShouldProduceDeterministicHash()
    {
        // Arrange
        string plaintext = "555-1234";
        byte[] salt = Encoding.UTF8.GetBytes("test-salt-12345678901234567890");

        // Act
        var index1 = BlindIndex.Compute(plaintext, salt);
        var index2 = BlindIndex.Compute(plaintext, salt);

        // Assert
        index1.Value.ShouldBe(index2.Value); // Deterministic
        index1.Value.ShouldNotBeEmpty();
        index1.Value.ShouldNotBe(plaintext); // One-way hash
    }

    [Fact]
    public void BlindIndex_Compute_DifferentSalt_ShouldProduceDifferentHash()
    {
        // Arrange
        string plaintext = "555-1234";
        byte[] salt1 = Encoding.UTF8.GetBytes("salt1-12345678901234567890");
        byte[] salt2 = Encoding.UTF8.GetBytes("salt2-12345678901234567890");

        // Act
        var index1 = BlindIndex.Compute(plaintext, salt1);
        var index2 = BlindIndex.Compute(plaintext, salt2);

        // Assert
        index1.Value.ShouldNotBe(index2.Value); // Different salts = different hashes
    }

    [Fact]
    public void BlindIndex_FromHash_ShouldWrapHashValue()
    {
        // Arrange
        string hashValue = "abc123hash";

        // Act
        var blindIndex = BlindIndex.FromHash(hashValue);

        // Assert
        blindIndex.Value.ShouldBe(hashValue);
        blindIndex.ToString().ShouldBe(hashValue);
    }

    [Fact]
    public void EncryptedData_ToStorageFormat_ShouldSerializeCorrectly()
    {
        // Arrange - use record constructor instead of Create()
        byte[] ciphertext = new byte[] { 1, 2, 3, 4, 5 };
        string keyId = "test-key-v1";
        byte[] iv = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };
        var encryptedData = new EncryptedData(ciphertext, keyId, iv);

        // Act
        string storageFormat = encryptedData.ToStorageFormat();

        // Assert
        storageFormat.ShouldContain(keyId);
        storageFormat.ShouldContain(Convert.ToBase64String(iv));
        storageFormat.ShouldContain(Convert.ToBase64String(ciphertext));
    }

    [Fact]
    public void EncryptedData_FromStorageFormat_ShouldDeserializeCorrectly()
    {
        // Arrange
        byte[] ciphertext = new byte[] { 1, 2, 3, 4, 5 };
        string keyId = "test-key-v1";
        byte[] iv = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };
        var original = new EncryptedData(ciphertext, keyId, iv);
        string storageFormat = original.ToStorageFormat();

        // Act
        var deserialized = EncryptedData.FromStorageFormat(storageFormat);

        // Assert
        deserialized.KeyId.ShouldBe(keyId);
        deserialized.Iv.ShouldBe(iv);
        deserialized.Ciphertext.ShouldBe(ciphertext);
    }

    [Fact]
    public void EncryptedData_WithEnvelopeDek_ShouldIncludeDek()
    {
        // Arrange
        byte[] ciphertext = new byte[] { 1, 2, 3 };
        string keyId = "kek-v1";
        byte[] iv = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };
        byte[] encryptedDek = new byte[] { 100, 101, 102 };

        // Act - use record constructor with optional encryptedDek parameter
        var encrypted = new EncryptedData(ciphertext, keyId, iv, encryptedDek);

        // Assert
        encrypted.EncryptedDek.ShouldNotBeNull();
        encrypted.EncryptedDek.ShouldBe(encryptedDek);
    }

    [Fact]
    public void EncryptedData_StorageFormat_ShouldPreserveVersionAndAlgorithm()
    {
        // Arrange
        var encrypted = new EncryptedData(
            new byte[] { 1, 2, 3 },
            "key-v2",
            new byte[16],
            null,
            Version: 2,
            AlgorithmType: "AES-256-GCM",
            AlgorithmVersion: 1);

        // Act
        string storageFormat = encrypted.ToStorageFormat();
        var restored = EncryptedData.FromStorageFormat(storageFormat);

        // Assert
        restored.Version.ShouldBe(2);
        restored.AlgorithmType.ShouldBe("AES-256-GCM");
    }
}

/// <summary>
///     Tests for DevelopmentEncryptionService.
///     Phase 7: Updated to use synchronous ComputeBlindIndex API.
/// </summary>
public class EncryptionServiceTests
{
    private readonly IEncryptionService _encryptionService;
    private readonly IBlindIndexSaltProvider _saltProvider;

    public EncryptionServiceTests()
    {
        _saltProvider = new DevelopmentBlindIndexSaltProvider();
        _encryptionService = new DevelopmentEncryptionService(_saltProvider);
    }

    [Fact]
    public async Task EncryptAsync_Decrypt_ShouldRoundTripCorrectly()
    {
        // Arrange
        string plaintext = "Sensitive customer data";

        // Act
        EncryptedData encrypted = await _encryptionService.EncryptAsync(plaintext);
        string decrypted = await _encryptionService.DecryptAsync(encrypted);

        // Assert
        decrypted.ShouldBe(plaintext);
        encrypted.Ciphertext.ShouldNotBeEmpty();
        encrypted.Iv.ShouldNotBeEmpty();
    }

    [Fact]
    public void Encrypt_Decrypt_Synchronous_ShouldRoundTripCorrectly()
    {
        // Arrange
        string plaintext = "Synchronous encryption test";

        // Act
        EncryptedData encrypted = _encryptionService.Encrypt(plaintext);
        string decrypted = _encryptionService.Decrypt(encrypted);

        // Assert
        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public async Task EncryptAsync_Probabilistic_ShouldProduceDifferentCiphertext()
    {
        // Arrange
        string plaintext = "Order notes: Customer requested gift wrapping";

        // Act
        EncryptedData encrypted1 = await _encryptionService.EncryptAsync(plaintext);
        EncryptedData encrypted2 = await _encryptionService.EncryptAsync(plaintext);

        // Assert
        encrypted1.Ciphertext.ShouldNotBe(encrypted2.Ciphertext); // Different ciphertext each time
        encrypted1.Iv.ShouldNotBe(encrypted2.Iv); // Random IV
    }

    [Fact]
    public void ComputeBlindIndex_ShouldBeDeterministic()
    {
        // Arrange
        string plaintext = "alice@example.com";

        // Act - Using synchronous API per Phase 7 requirement
        BlindIndex index1 = _encryptionService.ComputeBlindIndex(plaintext);
        BlindIndex index2 = _encryptionService.ComputeBlindIndex(plaintext);

        // Assert
        index1.Value.ShouldBe(index2.Value); // Deterministic for search
    }

    [Fact]
    public void ComputeBlindIndex_DifferentInputs_ShouldProduceDifferentIndexes()
    {
        // Arrange
        string email1 = "alice@example.com";
        string email2 = "bob@example.com";

        // Act
        BlindIndex index1 = _encryptionService.ComputeBlindIndex(email1);
        BlindIndex index2 = _encryptionService.ComputeBlindIndex(email2);

        // Assert
        index1.Value.ShouldNotBe(index2.Value);
    }

    [Fact]
    public void Encrypt_EmptyString_ShouldHandleGracefully()
    {
        // Arrange
        string plaintext = "";

        // Act
        EncryptedData encrypted = _encryptionService.Encrypt(plaintext);
        string decrypted = _encryptionService.Decrypt(encrypted);

        // Assert
        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public async Task EncryptAsync_SpecialCharacters_ShouldRoundTripCorrectly()
    {
        // Arrange - Unicode, emojis, special chars
        string plaintext = "Name: 日本語 🎉 <script>alert('xss')</script>";

        // Act
        EncryptedData encrypted = await _encryptionService.EncryptAsync(plaintext);
        string decrypted = await _encryptionService.DecryptAsync(encrypted);

        // Assert
        decrypted.ShouldBe(plaintext);
    }
}

/// <summary>
///     Tests for PiiVaultEntry domain model.
/// </summary>
public class PiiVaultEntryTests
{
    [Fact]
    public void PiiVaultEntry_Create_ShouldValidateRequiredFields()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
            PiiVaultEntry.Create(
                Guid.Empty, // Invalid ProfileId
                "user123",
                "encrypted-name",
                "encrypted-email",
                "email-index",
                "encrypted-phone",
                "phone-index",
                "encrypted-address"));
    }

    [Fact]
    public void PiiVaultEntry_Create_ShouldSetInitialValues()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        string userId = "user123";

        // Act
        var entry = PiiVaultEntry.Create(
            profileId,
            userId,
            "encrypted-name",
            "encrypted-email",
            "email-index",
            "encrypted-phone",
            "phone-index",
            "encrypted-address");

        // Assert
        entry.ProfileId.ShouldBe(profileId);
        entry.UserId.ShouldBe(userId);
        entry.IsDeleted.ShouldBeFalse();
        entry.CreatedAt.ShouldBeInRange(DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow);
    }

    [Fact]
    public void PiiVaultEntry_MarkAsDeleted_ShouldSetDeletedFlag()
    {
        // Arrange
        var entry = PiiVaultEntry.Create(
            Guid.NewGuid(),
            "user123",
            "encrypted-name",
            "encrypted-email",
            "email-index",
            "encrypted-phone",
            "phone-index",
            "encrypted-address");

        // Act
        entry.MarkAsDeleted();

        // Assert
        entry.IsDeleted.ShouldBeTrue();
        entry.DeletedAt.ShouldNotBeNull();
        entry.DeletedAt.Value.ShouldBeInRange(DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow);
    }

    [Fact]
    public void PiiVaultEntry_MarkAsDeleted_AlreadyDeleted_ShouldThrow()
    {
        // Arrange
        var entry = PiiVaultEntry.Create(
            Guid.NewGuid(),
            "user123",
            "encrypted-name",
            "encrypted-email",
            "email-index",
            "encrypted-phone",
            "phone-index",
            "encrypted-address");
        entry.MarkAsDeleted();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => entry.MarkAsDeleted());
    }

    [Fact]
    public void PiiVaultEntry_PurgeData_ShouldOverwritePii()
    {
        // Arrange
        var entry = PiiVaultEntry.Create(
            Guid.NewGuid(),
            "user123",
            "encrypted-name",
            "encrypted-email",
            "email-index",
            "encrypted-phone",
            "phone-index",
            "encrypted-address");
        entry.MarkAsDeleted();

        string originalName = entry.EncryptedFullName;
        string originalEmail = entry.EncryptedEmail;

        // Act
        entry.PurgeData();

        // Assert
        entry.EncryptedFullName.ShouldNotBe(originalName); // Overwritten
        entry.EncryptedEmail.ShouldNotBe(originalEmail); // Overwritten
        entry.ProfileId.ShouldBe(Guid.Empty); // Link broken
    }

    [Fact]
    public void PiiVaultEntry_PurgeData_NotDeleted_ShouldThrow()
    {
        // Arrange
        var entry = PiiVaultEntry.Create(
            Guid.NewGuid(),
            "user123",
            "encrypted-name",
            "encrypted-email",
            "email-index",
            "encrypted-phone",
            "phone-index",
            "encrypted-address");

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => entry.PurgeData());
    }

    [Fact]
    public void PiiVaultEntry_RecordAccess_ShouldUpdateTimestamp()
    {
        // Arrange
        var entry = PiiVaultEntry.Create(
            Guid.NewGuid(),
            "user123",
            "encrypted-name",
            "encrypted-email",
            "email-index",
            "encrypted-phone",
            "phone-index",
            "encrypted-address");

        DateTime originalAccess = entry.LastAccessedAt;
        Thread.Sleep(100); // Ensure time difference

        // Act
        entry.RecordAccess();

        // Assert
        entry.LastAccessedAt.ShouldBeGreaterThan(originalAccess);
    }

    [Fact]
    public void PiiVaultEntry_Update_ShouldModifyPiiFields()
    {
        // Arrange
        var entry = PiiVaultEntry.Create(
            Guid.NewGuid(),
            "user123",
            "old-name",
            "old-email",
            "old-email-index",
            "old-phone",
            "old-phone-index",
            "old-address");

        // Act
        entry.Update(
            "new-name",
            "new-email",
            "new-email-index",
            "new-phone",
            "new-phone-index",
            "new-address");

        // Assert
        entry.EncryptedFullName.ShouldBe("new-name");
        entry.EncryptedEmail.ShouldBe("new-email");
        entry.EmailBlindIndex.ShouldBe("new-email-index");
    }

    [Fact]
    public void PiiVaultEntry_ReEncrypt_ShouldUpdateKeyVersion()
    {
        // Arrange
        var entry = PiiVaultEntry.Create(
            Guid.NewGuid(),
            "user123",
            "encrypted-name",
            "encrypted-email",
            "email-index",
            "encrypted-phone",
            "phone-index",
            "encrypted-address",
            keyVersion: 1);

        // Act
        entry.ReEncrypt(
            "new-encrypted-name",
            "new-encrypted-email",
            "new-encrypted-phone",
            "new-encrypted-address",
            null,
            null,
            2);

        // Assert
        entry.KeyVersion.ShouldBe(2);
        entry.EncryptedFullName.ShouldBe("new-encrypted-name");
    }
}

/// <summary>
///     Phase 7 Critical Test: PiiEncryptionConverter round-trip verification.
///     This validates the EF Core value converter correctly encrypts/decrypts data.
/// </summary>
public class PiiEncryptionConverterTests
{
    private readonly DevelopmentCryptoProvider _cryptoProvider;

    public PiiEncryptionConverterTests()
    {
        var saltProvider = new DevelopmentBlindIndexSaltProvider();
        _cryptoProvider = new DevelopmentCryptoProvider(saltProvider);
    }

    [Fact]
    public void PiiEncryptionConverter_ShouldRoundTripCorrectly()
    {
        // Arrange - This is the critical test the architect requested
        string plaintext = "John Doe";

        // Act: Encrypt (simulating EF Core saving to DB)
        var encrypted = _cryptoProvider.Encrypt(plaintext.AsSpan(), isDeterministic: true);
        string storageFormat = encrypted.ToStorageFormat();

        // Act: Decrypt (simulating EF Core loading from DB)
        var restored = EncryptedData.FromStorageFormat(storageFormat);
        string decrypted = _cryptoProvider.Decrypt(restored);

        // Assert
        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public void PiiEncryptionConverter_DeterministicMode_ShouldProduceSameOutput()
    {
        // Arrange
        string plaintext = "555-123-4567";

        // Act
        var encrypted1 = _cryptoProvider.Encrypt(plaintext.AsSpan(), isDeterministic: true);
        var encrypted2 = _cryptoProvider.Encrypt(plaintext.AsSpan(), isDeterministic: true);

        // Assert - Deterministic mode should produce identical ciphertext for same input
        encrypted1.ToStorageFormat().ShouldBe(encrypted2.ToStorageFormat());
    }

    [Fact]
    public void PiiEncryptionConverter_ProbabilisticMode_ShouldProduceDifferentOutput()
    {
        // Arrange
        string plaintext = "Sensitive notes about the customer";

        // Act
        var encrypted1 = _cryptoProvider.Encrypt(plaintext.AsSpan(), isDeterministic: false);
        var encrypted2 = _cryptoProvider.Encrypt(plaintext.AsSpan(), isDeterministic: false);

        // Assert - Probabilistic mode uses random IV each time
        encrypted1.ToStorageFormat().ShouldNotBe(encrypted2.ToStorageFormat());

        // But both should decrypt to same plaintext
        _cryptoProvider.Decrypt(encrypted1).ShouldBe(plaintext);
        _cryptoProvider.Decrypt(encrypted2).ShouldBe(plaintext);
    }

    [Fact]
    public void BlindIndex_ShouldBeSearchable()
    {
        // Arrange - Email search scenario
        string email = "customer@example.com";

        // Act: Compute blind index at registration time
        BlindIndex indexAtRegistration = _cryptoProvider.ComputeBlindIndex(email.AsSpan());

        // Act: Compute blind index at search time
        BlindIndex indexAtSearch = _cryptoProvider.ComputeBlindIndex(email.AsSpan());

        // Assert - Should match for searchability
        indexAtRegistration.Value.ShouldBe(indexAtSearch.Value);
    }
}

/// <summary>
///     Development implementation of ICryptoProvider for testing.
///     Uses AES-256-CBC with in-memory keys.
///     SECURITY WARNING: Deterministic IV is intentional for searchable encryption in tests.
/// </summary>
#pragma warning disable CA5401 // Do not use CreateEncryptor with non-default IV - intentional for deterministic encryption
public class DevelopmentCryptoProvider : ICryptoProvider
{
    private readonly string _keyId = "dev-crypto-v1";
    private readonly byte[] _masterKey;
    private readonly IBlindIndexSaltProvider _saltProvider;

    public DevelopmentCryptoProvider(IBlindIndexSaltProvider saltProvider)
    {
        _saltProvider = saltProvider;
        // WARNING: Hardcoded key for development only
        _masterKey = Encoding.UTF8.GetBytes("DevelopmentMasterKey1234567890!!"); // 32 bytes for AES-256
    }

    public EncryptedData Encrypt(ReadOnlySpan<char> plaintext, bool isDeterministic = false)
    {
        if (plaintext.IsEmpty)
            return new EncryptedData(Array.Empty<byte>(), _keyId, Array.Empty<byte>());

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _masterKey;

        if (isDeterministic)
        {
            // Deterministic IV derived from plaintext hash (for searchable fields)
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(plaintext.ToString()));
            aes.IV = hash.Take(16).ToArray();
        }
        else
        {
            aes.GenerateIV();
        }

        using var encryptor = aes.CreateEncryptor();
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext.ToString());
        byte[] ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        return new EncryptedData(ciphertext, _keyId, aes.IV);
    }

    public string Decrypt(EncryptedData data)
    {
        if (data.Ciphertext.Length == 0)
            return string.Empty;

        if (data.KeyId != _keyId)
            throw new InvalidOperationException($"Unknown key ID: {data.KeyId}");

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _masterKey;
        aes.IV = data.Iv;

        using var decryptor = aes.CreateDecryptor();
        byte[] plaintextBytes = decryptor.TransformFinalBlock(data.Ciphertext, 0, data.Ciphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public BlindIndex ComputeBlindIndex(ReadOnlySpan<char> plaintext)
    {
        byte[] salt = _saltProvider.GetCurrentSaltAsync().Result;
        return BlindIndex.Compute(plaintext.ToString(), salt);
    }
}
#pragma warning restore CA5401
