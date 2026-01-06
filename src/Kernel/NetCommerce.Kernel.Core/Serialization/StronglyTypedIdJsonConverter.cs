using System.Text.Json;
using System.Text.Json.Serialization;
using NetCommerce.Kernel.Core.Ids;

namespace NetCommerce.Kernel.Core.Serialization;

/// <summary>
///     JSON converter for strongly typed IDs.
/// </summary>
public class StronglyTypedIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct, IStronglyTypedId, IParsable<TId>
{
    /// <inheritdoc />
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            throw new JsonException($"Cannot convert null or empty string to {typeof(TId).Name}");
        }

        if (TId.TryParse(value, null, out var result))
        {
            return result;
        }

        throw new JsonException($"Cannot convert '{value}' to {typeof(TId).Name}");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value.ToString());
    }
}
