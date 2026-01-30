using Microsoft.Extensions.Options;

namespace NetCommerce.Kernel.AspNetCore;

/// <summary>
/// Service for generating RFC 9457 Problem Details type URIs.
/// Centralizes the logic for constructing problem type URLs.
/// </summary>
public sealed class ProblemDetailsUriGenerator
{
    private readonly ProblemDetailsOptions _options;

    public ProblemDetailsUriGenerator(IOptions<ProblemDetailsOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Gets the configured base URI for problem type URIs.
    /// </summary>
    public string BaseUri => _options.BaseUri.TrimEnd('/');

    /// <summary>
    /// Generates a problem type URI from an error code.
    /// Converts error codes like "Validation.Error" to URLs like "https://netcommerce.io/problems/validation-error".
    /// </summary>
    public string GenerateTypeUri(string errorCode)
    {
        var slug = errorCode.ToLowerInvariant().Replace('.', '-').Replace('_', '-');
        return $"{BaseUri}/{slug}";
    }
}
