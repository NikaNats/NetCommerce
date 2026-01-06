#nullable enable
using System.Text.RegularExpressions;

namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
///     Centralized slug generation utility following DRY principle.
///     Generates URL-friendly slugs from input strings.
/// </summary>
public static partial class SlugGenerator
{
    /// <summary>
    ///     Generates a URL-friendly slug from the given text.
    /// </summary>
    /// <param name="text">The text to convert to a slug.</param>
    /// <returns>A lowercase, hyphenated slug.</returns>
    public static string Generate(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        // Convert to lowercase
        var slug = text.ToLowerInvariant();

        // Replace common special characters with words
        slug = slug.Replace("&", "and");

        // Remove quotes and apostrophes
        slug = slug.Replace("'", "")
            .Replace("\"", "");

        // Replace spaces and underscores with hyphens
        slug = slug.Replace(" ", "-")
            .Replace("_", "-");

        // Remove any characters that are not alphanumeric or hyphens
        slug = NonAlphanumericRegex().Replace(slug, "");

        // Replace multiple consecutive hyphens with a single hyphen
        slug = MultipleHyphensRegex().Replace(slug, "-");

        // Trim hyphens from start and end
        slug = slug.Trim('-');

        return slug;
    }

    /// <summary>
    ///     Generates a unique slug by appending a suffix if needed.
    /// </summary>
    /// <param name="text">The text to convert to a slug.</param>
    /// <param name="existingSlugs">Collection of already existing slugs to check against.</param>
    /// <returns>A unique slug.</returns>
    public static string GenerateUnique(string text, IReadOnlyCollection<string> existingSlugs)
    {
        var baseSlug = Generate(text);

        if (!existingSlugs.Contains(baseSlug))
            return baseSlug;

        var counter = 1;
        string uniqueSlug;
        do
        {
            uniqueSlug = $"{baseSlug}-{counter}";
            counter++;
        } while (existingSlugs.Contains(uniqueSlug));

        return uniqueSlug;
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex("-+")]
    private static partial Regex MultipleHyphensRegex();
}
