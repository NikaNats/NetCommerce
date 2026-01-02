using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetCommerce.SharedKernel.Infrastructure.Serialization;

/// <summary>
///     Shared JSON serialization options for consistent formatting across the application.
///     Uses source generation where possible for improved performance.
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}