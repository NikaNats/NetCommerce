#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetCommerce.Kernel.Core.Ids;

namespace NetCommerce.Kernel.Core.Serialization;

/// <summary>
///     Factory to create the converter for any type implementing IStronglyTypedId.
/// </summary>
public class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Check if it's a value type implementing our generic interface
        return typeToConvert.IsValueType &&
               typeToConvert.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStronglyTypedId<>));
    }

    public override JsonConverter? CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var converterType = typeof(StronglyTypedIdJsonConverter<>).MakeGenericType(typeToConvert);

        // This is the only spot using Activator, effectively unavoidable for generic factories.
        // However, it runs ONCE per application startup per type, so performance impact is zero.
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
///     The actual generic converter.
///     Uses static abstract interface members for Zero-Allocation parsing.
/// </summary>
public class StronglyTypedIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct, IStronglyTypedId<TId>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.String)
        {
            // Optimization: Try parsing Guid directly from UTF8 bytes
            // to avoid allocating a string on the heap.
            if (reader.TryGetGuid(out var guid))
            {
                return TId.Create(guid);
            }

            // Fallback
            var stringValue = reader.GetString();
            if (Guid.TryParse(stringValue, out var parsedGuid))
            {
                return TId.Create(parsedGuid);
            }
        }

        throw new JsonException($"Unable to convert JSON to {typeof(TId).Name}");
    }

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }

    // Handles using the ID as a Dictionary Key: Map<OrderId, OrderItem>
    public override TId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var guidString = reader.GetString();
        if (Guid.TryParse(guidString, out var guid))
        {
            return TId.Create(guid);
        }

        throw new JsonException($"Unable to convert property name to {typeof(TId).Name}");
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.Value.ToString());
    }
}
