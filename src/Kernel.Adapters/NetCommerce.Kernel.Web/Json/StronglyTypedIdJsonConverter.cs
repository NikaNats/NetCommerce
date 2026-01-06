#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetCommerce.Kernel.Web.Json;

/// <summary>
/// JSON converter for Strongly Typed IDs to serialize/deserialize as simple values.
/// Converts {"id": {"value": "..."}} to {"id": "..."}.
/// </summary>
public class StronglyTypedIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct // Assuming record struct usage
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (stringValue != null)
            {
                // Try to create instance using the Value property
                var valueProperty = typeToConvert.GetProperty("Value");
                if (valueProperty != null)
                {
                    var valueType = valueProperty.PropertyType;
                    if (valueType == typeof(Guid))
                    {
                        var guid = Guid.Parse(stringValue);
                        return (TId)Activator.CreateInstance(typeToConvert, guid)!;
                    }
                    else if (valueType == typeof(string))
                    {
                        return (TId)Activator.CreateInstance(typeToConvert, stringValue)!;
                    }
                    else if (valueType == typeof(int))
                    {
                        var intValue = int.Parse(stringValue);
                        return (TId)Activator.CreateInstance(typeToConvert, intValue)!;
                    }
                    // Add more types as needed
                }
            }
        }

        throw new JsonException($"Cannot convert JSON value to {typeToConvert.Name}");
    }

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        // Extract Value property dynamically
        var property = value.GetType().GetProperty("Value");
        var propertyValue = property?.GetValue(value);

        if (propertyValue != null)
        {
            writer.WriteStringValue(propertyValue.ToString());
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
