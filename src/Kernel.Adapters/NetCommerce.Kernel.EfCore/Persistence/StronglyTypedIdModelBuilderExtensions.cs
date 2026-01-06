#nullable enable
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetCommerce.Kernel.Core.Ids;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Extension methods for automatic strongly typed ID converter configuration.
/// </summary>
public static class StronglyTypedIdModelBuilderExtensions
{
    /// <summary>
    ///     Automatically applies EfValueConverter to all properties of strongly typed ID types.
    ///     This method looks for nested EfValueConverter classes within strongly typed ID record structs.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ConfigureStronglyTypedIdConverters(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                // Check if the property type implements IStronglyTypedId
                if (typeof(IStronglyTypedId).IsAssignableFrom(property.ClrType))
                {
                    // Try to find the nested EfValueConverter class
                    var converterType = property.ClrType.GetNestedType("EfValueConverter");
                    if (converterType is not null)
                    {
                        // Create an instance of the converter
                        var converterInstance = Activator.CreateInstance(converterType);
                        if (converterInstance is not null)
                        {
                            // Apply the converter to the property
                            property.SetValueConverter((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)converterInstance);
                        }
                    }
                }
            }
        }
    }
}
