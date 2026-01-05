#nullable enable

using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Security;
using Shouldly;
using Xunit;

namespace NetCommerce.Domain.Tests.Privacy;

/// <summary>
///     Tests for encryption primitives: BlindIndex, EncryptedData, SecureValue.
/// </summary>
public class EncryptionPrimitivesTests
{
    [Fact]
    public void BlindIndex_Compute_ShouldProduceDeterministicHash()
    {
        // Arrange
        var plaintext = "555-1234";
        var salt = System.Text.Encoding.UTF8.GetBytes("test-salt-12345678901234567890");

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
        var plaintext = "555-1234";
        var salt1 = System.Text.Encoding.UTF8.GetBytes("salt1-12345678901234567890");
        var salt2 = System.Text.Encoding.UTF8.GetBytes("salt2-12345678901234567890");

        // Act
        var index1 = BlindIndex.Compute(plaintext, salt1);
        var index2 = BlindIndex.Compute(plaintext, salt2);

        // Assert
        index1.Value.ShouldNotBe(index2.Value); // Different salts = different hashes
    }

    [Fact]
    public void EncryptedData_ToStorageFormat_ShouldSerializeCorrectly()
    {
        // Arrange
        var ciphertext = new byte[] { 1, 2, 3, 4, 5 };
        var keyId = "test-key-v1";
        var iv = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };
        var encryptedData = EncryptedData.Create(ciphertext, keyId, iv);

        // Act
        var storageFormat = encryptedData.ToStorageFormat();

        // Assert
        storageFormat.ShouldContain(keyId);
        storageFormat.ShouldContain(Convert.ToBase64String(iv));
        storageFormat.ShouldContain(Convert.ToBase64String(ciphertext));
    }

    [Fact]
    public void EncryptedData_FromStorageFormat_ShouldDeserializeCorrectly()
    {
        // Arrange
        var ciphertext = new byte[] { 1, 2, 3, 4, 5 };
        var keyId = "test-key-v1";
        var iv = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };
        var original = EncryptedData.Create(ciphertext, keyId, iv);
        var storageFormat = original.ToStorageFormat();

        // Act
        var deserialized = EncryptedData.FromStorageFormat(storageFormat);

        // Assert
        deserialized.KeyId.ShouldBe(keyId);
        deserialized.Iv.ShouldBe(iv);
        deserialized.Ciphertext.ShouldBe(ciphertext);
    }

    [Fact]
    public void EncryptedData_CreateWithEnvelope_ShouldIncludeDek()
    {
        // Arrange
        var ciphertext = new byte[] { 1, 2, 3 };
        var keyId = "kek-v1";
        var iv = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };
        var encryptedDek = new byte[] { 100, 101, 102 };

        // Act
        var encrypted = EncryptedData.CreateWithEnvelope(ciphertext, keyId, iv, encryptedDek);

        // Assert
        encrypted.EncryptedDek.ShouldNotBeNull();
        encrypted.EncryptedDek.ShouldBe(encryptedDek);
    }
}

/// <summary>
///     Tests for DevelopmentEncryptionService.
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
        var plaintext = "Sensitive customer data";

        // Act
        var encrypted = await _encryptionService.EncryptAsync(plaintext);
        var decrypted = await _encryptionService.DecryptAsync(encrypted);

        // Assert
        decrypted.ShouldBe(plaintext);
        encrypted.Ciphertext.ShouldNotBeEmpty();
        encrypted.Iv.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task EncryptAsync_Deterministic_ShouldProduceSameCiphertext()
    {
        // Arrange
        var plaintext = "555-1234";

        // Act
        var encrypted1 = await _encryptionService.EncryptAsync(plaintext, isDeterministic: true);
        var encrypted2 = await _encryptionService.EncryptAsync(plaintext, isDeterministic: true);

        // Assert
        encrypted1.Ciphertext.ShouldBe(encrypted2.Ciphertext); // Same input → same output
        encrypted1.Iv.ShouldBe(encrypted2.Iv); // Deterministic IV
    }

    [Fact]
    public async Task EncryptAsync_Probabilistic_ShouldProduceDifferentCiphertext()
    {
        // Arrange
        var plaintext = "Order notes: Customer requested gift wrapping";

        // Act
        var encrypted1 = await _encryptionService.EncryptAsync(plaintext, isDeterministic: false);
        var encrypted2 = await _encryptionService.EncryptAsync(plaintext, isDeterministic: false);

        // Assert
        encrypted1.Ciphertext.ShouldNotBe(encrypted2.Ciphertext); // Different ciphertext each time
        encrypted1.Iv.ShouldNotBe(encrypted2.Iv); // Random IV
    }

    [Fact]
    public async Task ComputeBlindIndexAsync_ShouldBeDeterministic()
    {
        // Arrange
        var plaintext = "alice@example.com";

        // Act
        var index1 = await _encryptionService.ComputeBlindIndexAsync(plaintext);
        var index2 = await _encryptionService.ComputeBlindIndexAsync(plaintext);

        // Assert
        index1.Value.ShouldBe(index2.Value); // Deterministic for search
    }

    [Fact]
    public async Task CreateSecureValueAsync_ShouldIncludeEncryptedAndBlindIndex()
    {
        // Arrange
        var plaintext = "555-1234";

        // Act
        var secureValue = await _encryptionService.CreateSecureValueAsync(
            plaintext,
            isDeterministic: true);

        // Assert
        secureValue.Encrypted.ShouldNotBeNull();
        secureValue.SearchIndex.ShouldNotBeNull();

        var decrypted = await _encryptionService.DecryptAsync(secureValue.Encrypted);
        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public async Task ReEncryptAsync_ShouldPreservePlaintext()
    {
        // Arrange
        var plaintext = "Original sensitive data";
        var encrypted = await _encryptionService.EncryptAsync(plaintext);

        // Act
        var reEncrypted = await _encryptionService.ReEncryptAsync(encrypted);
        var decrypted = await _encryptionService.DecryptAsync(reEncrypted);

        // Assert
        decrypted.ShouldBe(plaintext);
        reEncrypted.Ciphertext.ShouldNotBe(encrypted.Ciphertext); // Different ciphertext
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
        var userId = "user123";

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

        var originalName = entry.EncryptedFullName;
        var originalEmail = entry.EncryptedEmail;

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

        var originalAccess = entry.LastAccessedAt;
        System.Threading.Thread.Sleep(100); // Ensure time difference

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
            newKeyVersion: 2);

        // Assert
        entry.KeyVersion.ShouldBe(2);
        entry.EncryptedFullName.ShouldBe("new-encrypted-name");
    }
}
