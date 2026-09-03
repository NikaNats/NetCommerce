#nullable enable

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence.Repositories;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Pii;
using NetCommerce.Kernel.Core.Encryption;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Integration.Tests.Security;

/// <summary>
///     Blind index collision &amp; PII vault isolation penetration tests, executed
///     against the real <c>finance.pii_vault_entries</c> store:
///     1. Blind indexes are deterministic and ciphertext round-trips, using the
///        production <see cref="BlindIndex"/> / <see cref="EncryptedData"/>
///        primitives with a development-only cipher.
///     2. The kernel global query filters (<c>ApplyKernelGlobalFilters</c>)
///        enforce strict multi-tenant isolation on vault rows: Tenant B cannot
///        resolve Tenant A's entry even with the exact blind index, and
///        GDPR-forgotten rows are invisible to LINQ by default.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "SecurityPenetration")]
public sealed class PiiVaultIsolationPenetrationTests : IntegrationTestBase
{
    private static readonly byte[] BlindIndexSalt = SHA256.HashData(Encoding.UTF8.GetBytes("NetCommerce.PiiVaultIsolationPenetrationTests.v1"));

    public PiiVaultIsolationPenetrationTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public void BlindIndex_MustBeDeterministic_AndCiphertextMustRoundTrip()
    {
        var victimEmail = "ceo@victim-enterprise.com";

        // 1. Determinism: same input must produce an identical index for searchability.
        var indexAtRegistration = BlindIndex.Compute(victimEmail, BlindIndexSalt);
        var indexAtSearch = BlindIndex.Compute(victimEmail, BlindIndexSalt);
        indexAtRegistration.Value.ShouldBe(indexAtSearch.Value);

        // A different input must NOT collide with the victim index.
        var otherIndex = BlindIndex.Compute("attacker@evil.example", BlindIndexSalt);
        otherIndex.Value.ShouldNotBe(indexAtRegistration.Value);

        // 2. Ciphertext produced for vault storage must decrypt back to the plaintext.
        var encryptedEmail = DevCipher.Encrypt(victimEmail, isDeterministic: true);
        var restored = EncryptedData.FromStorageFormat(encryptedEmail.ToStorageFormat());
        DevCipher.Decrypt(restored).ShouldBe(victimEmail);

        // Deterministic mode must be stable; probabilistic mode must differ per encryption.
        DevCipher.Encrypt(victimEmail, isDeterministic: true).ToStorageFormat()
            .ShouldBe(encryptedEmail.ToStorageFormat());
        DevCipher.Encrypt("probabilistic note", isDeterministic: false).ToStorageFormat()
            .ShouldNotBe(DevCipher.Encrypt("probabilistic note", isDeterministic: false).ToStorageFormat());
    }

    [Fact]
    public async Task PiiVault_CrossTenantBlindIndexQuery_MustNeverLeakVictimData()
    {
        const string tenantVictim = "tenant-victim-corp";
        const string tenantAttacker = "tenant-attacker-inc";
        var victimEmail = "ceo@victim-enterprise.com";
        var victimPhone = "+15559990000";
        var victimProfileId = Guid.NewGuid();

        // Victim's opaque lookup tokens, derived exactly like vault blind indexes.
        var emailBlindIndex = BlindIndex.Compute(victimEmail, BlindIndexSalt).Value;
        var phoneBlindIndex = BlindIndex.Compute(victimPhone, BlindIndexSalt).Value;

        // 1. Seed the victim vault entry under the victim tenant.
        await using (var victimDb = CreateTenantFinanceDb(tenantVictim))
        {
            var entry = PiiVaultEntry.Create(
                victimProfileId,
                "user_victim_999",
                DevCipher.Encrypt("Victim CEO", isDeterministic: false).ToStorageFormat(),
                DevCipher.Encrypt(victimEmail, isDeterministic: true).ToStorageFormat(),
                emailBlindIndex,
                DevCipher.Encrypt(victimPhone, isDeterministic: true).ToStorageFormat(),
                phoneBlindIndex,
                encryptedAddress: DevCipher.Encrypt("Confidential Address", isDeterministic: false).ToStorageFormat(),
                keyVersion: 1,
                tenantId: tenantVictim);

            var repo = new PiiVaultRepository(victimDb);
            await repo.AddAsync(entry);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ATTACK PHASE: Attacker in Tenant B queries the real vault repository
        // using the victim's computed blind index.
        // ═══════════════════════════════════════════════════════════════════════
        await using (var attackerDb = CreateTenantFinanceDb(tenantAttacker))
        {
            var attackerRepo = new PiiVaultRepository(attackerDb);

            var byIndex = await attackerRepo.FindByBlindIndexAsync(
                nameof(PiiVaultEntry.EmailBlindIndex), emailBlindIndex);

            // 2. ASSERT: Record exists in database, but attacker queries return nothing
            byIndex.ShouldBeEmpty("CRITICAL SECURITY LEAK: Attacker resolved victim PII via blind index!");

            var direct = await attackerDb.Set<PiiVaultEntry>()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.EmailBlindIndex == emailBlindIndex);
            direct.ShouldBeNull("CRITICAL SECURITY LEAK: Attacker queried victim PII via blind index!");
        }

        // 3. Verify the victim tenant resolves its own record and decrypts it.
        await using (var victimDb = CreateTenantFinanceDb(tenantVictim))
        {
            var victimRepo = new PiiVaultRepository(victimDb);
            var legitimateRecord = await victimRepo.FindByProfileIdAsync(victimProfileId);

            legitimateRecord.ShouldNotBeNull();
            legitimateRecord.TenantId.ShouldBe(tenantVictim);
            var decryptedEmail = DevCipher.Decrypt(EncryptedData.FromStorageFormat(legitimateRecord.EncryptedEmail));
            decryptedEmail.ShouldBe(victimEmail);
        }

        // 4. GDPR erasure must hide the row from LINQ even for the owning tenant.
        await using (var victimDb = CreateTenantFinanceDb(tenantVictim))
        {
            var victimRepo = new PiiVaultRepository(victimDb);
            await victimRepo.DeleteAsync(victimProfileId);

            var forgotten = await victimRepo.FindByProfileIdAsync(victimProfileId);
            forgotten.ShouldBeNull("Forgotten vault entry is still visible to LINQ queries.");
        }
    }

    /// <summary>
    ///     Builds a <see cref="FinanceDbContext"/> bound to the given tenant,
    ///     reusing the host's configured options (Npgsql + kernel interceptors).
    ///     The caller owns disposal; each context gets its own DI scope.
    /// </summary>
    private FinanceDbContext CreateTenantFinanceDb(string tenantId)
    {
        var scope = Fixture.Host.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<FinanceDbContext>>();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        tenantContext.HasTenant.Returns(true);

        // Scope lifetime is tied to the context: disposing the context must not
        // leak the scope. The context does not own the scope, so dispose both.
        var db = new TenantFinanceDbContext(options, tenantContext, scope);
        return db;
    }

    private sealed class TenantFinanceDbContext : FinanceDbContext, IDisposable
    {
        private readonly IServiceScope _scope;

        public TenantFinanceDbContext(
            DbContextOptions<FinanceDbContext> options,
            ITenantContext tenantContext,
            IServiceScope scope)
            : base(options, tenantContext)
        {
            _scope = scope;
        }

        public override void Dispose()
        {
            base.Dispose();
            _scope.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (_scope is IAsyncDisposable asyncScope)
                await asyncScope.DisposeAsync();
            else
                _scope.Dispose();
        }
    }

    /// <summary>
    ///     Development-only cipher mirroring the semantics of the
    ///     <c>DevelopmentCryptoProvider</c> used in Domain.Tests: AES-256-CBC
    ///     with a plaintext-derived IV in deterministic mode and a random IV
    ///     otherwise. Exists here so this suite stays self-contained instead of
    ///     taking a test-project-to-test-project reference.
    /// </summary>
    private static class DevCipher
    {
        private const string KeyId = "dev-pii-penetration-v1";
        private static readonly byte[] MasterKey = SHA256.HashData(Encoding.UTF8.GetBytes("PiiVaultIsolationPenetrationTests.MasterKey"))[..32];

        // Intentional deterministic IV mirrors DevelopmentCryptoProvider semantics
        // (searchable encryption); same justification as the Domain.Tests suite.
#pragma warning disable CA5401 // Deterministic IV is the point under test
        public static EncryptedData Encrypt(string plaintext, bool isDeterministic)
        {
            using var aes = Aes.Create();
            aes.Key = MasterKey;
            aes.IV = isDeterministic
                ? SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))[..16]
                : RandomNumberGenerator.GetBytes(16);

            using var encryptor = aes.CreateEncryptor();
            var cipher = encryptor.TransformFinalBlock(Encoding.UTF8.GetBytes(plaintext), 0, Encoding.UTF8.GetByteCount(plaintext));
            return new EncryptedData(cipher, KeyId, aes.IV);
        }
#pragma warning restore CA5401

        public static string Decrypt(EncryptedData data)
        {
            using var aes = Aes.Create();
            aes.Key = MasterKey;
            aes.IV = data.Iv;

            using var decryptor = aes.CreateDecryptor();
            var plain = decryptor.TransformFinalBlock(data.Ciphertext, 0, data.Ciphertext.Length);
            return Encoding.UTF8.GetString(plain);
        }
    }
}
