#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Kernel.Compliance.Encryption;
using NetCommerce.SharedKernel.Infrastructure.Security;

namespace NetCommerce.SharedKernel.Application;

/// <summary>
/// Extension methods for configuring enterprise-grade encryption services.
/// </summary>
public static class EncryptionServiceCollectionExtensions
{
    /// <summary>
    /// Adds enterprise encryption services with envelope encryption pattern.
    /// Uses AES-GCM with hardware acceleration and DEK caching for optimal performance.
    /// </summary>
    public static IServiceCollection AddEnterpriseEncryption(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Configuration
        services.Configure<EncryptionOptions>(configuration.GetSection("Security:Encryption"));

        // 2. Caching (Required for DekCache)
        services.AddMemoryCache();

        // 3. Key Management (Cloud integration - replace with real implementation)
        services.AddSingleton<IKeyManagementService, DevelopmentKeyManagementServiceV2>();

        // 4. Infrastructure (High-performance crypto)
        services.AddSingleton<DekCache>();
        services.AddSingleton<ICryptoProvider, AesGcmCryptoProvider>();

        // 5. Legacy compatibility (if needed)
        services.AddSingleton<IEncryptionService, LegacyEncryptionServiceAdapter>();

        return services;
    }
}

/// <summary>
/// Adapter to provide backward compatibility with existing IEncryptionService consumers.
/// Wraps the new ICryptoProvider with async methods.
/// </summary>
internal class LegacyEncryptionServiceAdapter : IEncryptionService
{
    private readonly ICryptoProvider _cryptoProvider;

    public LegacyEncryptionServiceAdapter(ICryptoProvider cryptoProvider)
    {
        _cryptoProvider = cryptoProvider;
    }

    public Task<NetCommerce.Kernel.Core.Encryption.EncryptedData> EncryptAsync(
        string plaintext,
        bool isDeterministic = false,
        CancellationToken cancellationToken = default)
    {
        var result = _cryptoProvider.Encrypt(plaintext.AsSpan(), isDeterministic);
        return Task.FromResult(result);
    }

    public Task<string> DecryptAsync(
        NetCommerce.Kernel.Core.Encryption.EncryptedData encryptedData,
        CancellationToken cancellationToken = default)
    {
        var result = _cryptoProvider.Decrypt(encryptedData);
        return Task.FromResult(result);
    }

    public NetCommerce.Kernel.Core.Encryption.EncryptedData Encrypt(string plaintext, bool isDeterministic = false)
    {
        return _cryptoProvider.Encrypt(plaintext.AsSpan(), isDeterministic);
    }

    public string Decrypt(NetCommerce.Kernel.Core.Encryption.EncryptedData encryptedData)
    {
        return _cryptoProvider.Decrypt(encryptedData);
    }

    public NetCommerce.Kernel.Core.Encryption.BlindIndex ComputeBlindIndex(string plaintext)
    {
        // For now, return empty - blind indexes need separate implementation
        // In production, you'd want a proper blind index service
        return new NetCommerce.Kernel.Core.Encryption.BlindIndex(string.Empty);
    }
}
