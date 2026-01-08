#nullable enable
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetCommerce.Kernel.Compliance.Encryption;
using NetCommerce.Kernel.Compliance.Pii;
using NetCommerce.Kernel.EfCore.Converters;
using NetCommerce.Kernel.Core.Encryption; // Required for typeof(BlindIndex)

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Extension methods for automatic PII encryption configuration.
/// </summary>
public static class PiiModelBuilderExtensions
{
    /// <summary>
    ///     Automatically applies PiiEncryptionConverter to all properties marked with [PiiSensitive].
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="cryptoProvider">The crypto provider to use for encryption.</param>
    public static void ConfigurePiiEncryption(this ModelBuilder modelBuilder, ICryptoProvider cryptoProvider)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var piiAttribute = property.PropertyInfo?.GetCustomAttribute<PiiSensitiveAttribute>();

                if (piiAttribute is not null && property.ClrType == typeof(string))
                {
                    // Apply PiiEncryptionConverter
                    var converter = new PiiEncryptionConverter(cryptoProvider, piiAttribute.IsDeterministic);
                    property.SetValueConverter(converter);

                    // Configure blind index if specified
                    if (!string.IsNullOrWhiteSpace(piiAttribute.BlindIndexColumnName))
                    {
                        // Look for a BlindIndex property with the specified name
                        var blindIndexProperty = entityType.GetProperties()
                            .FirstOrDefault(p => p.Name == piiAttribute.BlindIndexColumnName);

                        if (blindIndexProperty is not null)
                        {
                            // FIX: Safety check to prevent crashing if user defined property as 'string'
                            if (blindIndexProperty.ClrType == typeof(NetCommerce.Kernel.Core.Encryption.BlindIndex))
                            {
                                blindIndexProperty.SetValueConverter(new BlindIndexValueConverter());
                            }
                            else
                            {
                                // Optional: Log warning or throw specific error
                                // For now, we skip or throw to enforce type safety
                                throw new InvalidOperationException(
                                    $"Blind Index property '{blindIndexProperty.Name}' on '{entityType.Name}' must be of type 'NetCommerce.Kernel.Core.Encryption.BlindIndex'. Found: '{blindIndexProperty.ClrType.Name}'.");
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Automatically applies PiiEncryptionConverter to all properties marked with [PiiSensitive].
    ///     Resolves ICryptoProvider from the service provider.
    /// </summary>
    public static void ConfigurePiiEncryption(this ModelBuilder modelBuilder, IServiceProvider serviceProvider)
    {
        var cryptoProvider = serviceProvider.GetService(typeof(ICryptoProvider)) as ICryptoProvider;

        if (cryptoProvider is null)
        {
            throw new InvalidOperationException(
                "ICryptoProvider not registered. Call services.AddEnterpriseEncryption() first.");
        }

        modelBuilder.ConfigurePiiEncryption(cryptoProvider);
    }
}
