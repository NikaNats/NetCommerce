#nullable enable
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetCommerce.Kernel.Compliance.Encryption;
using NetCommerce.Kernel.Compliance.Pii;
using NetCommerce.Kernel.EfCore.Converters;

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
    /// <param name="encryptionService">The encryption service to use.</param>
    public static void ConfigurePiiEncryption(this ModelBuilder modelBuilder, IEncryptionService encryptionService)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var piiAttribute = property.PropertyInfo?.GetCustomAttribute<PiiSensitiveAttribute>();

                if (piiAttribute is not null && property.ClrType == typeof(string))
                {
                    // Apply PiiEncryptionConverter
                    var converter = new PiiEncryptionConverter(encryptionService, piiAttribute.IsDeterministic);
                    property.SetValueConverter(converter);

                    // Configure blind index if specified
                    if (!string.IsNullOrWhiteSpace(piiAttribute.BlindIndexColumnName))
                    {
                        // Look for a BlindIndex property with the specified name
                        var blindIndexProperty = entityType.GetProperties()
                            .FirstOrDefault(p => p.Name == piiAttribute.BlindIndexColumnName);

                        if (blindIndexProperty is not null)
                        {
                            blindIndexProperty.SetValueConverter(new BlindIndexConverter());
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Automatically applies PiiEncryptionConverter to all properties marked with [PiiSensitive].
    ///     Resolves IEncryptionService from the service provider.
    /// </summary>
    public static void ConfigurePiiEncryption(this ModelBuilder modelBuilder, IServiceProvider serviceProvider)
    {
        var encryptionService = serviceProvider.GetService(typeof(IEncryptionService)) as IEncryptionService;

        if (encryptionService is null)
        {
            throw new InvalidOperationException(
                "IEncryptionService not registered. Call services.AddSingleton<IEncryptionService, YourImplementation>() first.");
        }

        modelBuilder.ConfigurePiiEncryption(encryptionService);
    }
}
