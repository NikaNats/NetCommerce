#nullable enable
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace NetCommerce.Kernel.EfCore.Converters;

/// <summary>
/// Universal Fallback Converter.
/// NOTE: Uses Reflection. For high-performance and Native AOT,
/// use the Source Generated nested EfValueConverter within the ID record.
/// </summary>
public class StronglyTypedIdFallbackConverter<TId, TValue> : ValueConverter<TId, TValue>
    where TId : struct
    where TValue : notnull
{
    public StronglyTypedIdFallbackConverter()
        : base(id => GetValue(id), value => Create(value)) { }

    private static TValue GetValue(TId id)
    {
        return (TValue)typeof(TId).GetProperty("Value")!.GetValue(id)!;
    }

    private static TId Create(TValue value)
    {
        return (TId)Activator.CreateInstance(typeof(TId), value)!;
    }
}