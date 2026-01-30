namespace NetCommerce.Kernel.AspNetCore;

/// <summary>
/// Configuration options for RFC 9457 Problem Details responses.
/// </summary>
public sealed class ProblemDetailsOptions
{
    /// <summary>
    /// Gets or sets the base URI for problem type URIs.
    /// Should point to documentation explaining the errors.
    /// </summary>
    /// <example>
    /// Development: "http://localhost:5000/docs/problems"
    /// Production: "https://docs.netcommerce.io/problems"
    /// </example>
    public string BaseUri { get; set; } = "https://netcommerce.io/problems";

    /// <summary>
    /// Gets or sets whether to include stack traces in problem details.
    /// Should be false in production for security reasons.
    /// </summary>
    public bool IncludeStackTrace { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include exception details in problem details.
    /// Should be false in production for security reasons.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; } = false;
}
