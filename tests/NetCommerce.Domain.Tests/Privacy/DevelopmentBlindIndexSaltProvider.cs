#nullable enable
using System.Text;
using NetCommerce.Kernel.Compliance.Encryption;

namespace NetCommerce.Domain.Tests.Privacy;

/// <summary>
///     Development/Testing implementation of IBlindIndexSaltProvider.
///     SECURITY WARNING:
///     This implementation uses a hardcoded salt and is NOT suitable for production.
///     For Production:
///     - Store salt in Azure Key Vault or AWS Secrets Manager
///     - Rotate salt annually
///     - Use different salts for different tenants/regions
/// </summary>
public class DevelopmentBlindIndexSaltProvider : IBlindIndexSaltProvider
{
    private const int CurrentVersion = 1;
    private readonly byte[] _salt;

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

    public Task<byte[]?> GetSaltByVersionAsync(int version, CancellationToken cancellationToken = default)
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
