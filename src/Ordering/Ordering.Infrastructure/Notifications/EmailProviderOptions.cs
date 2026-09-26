#nullable enable
namespace NetCommerce.Ordering.Infrastructure.Notifications;

/// <summary>
///     Selects the <c>IEmailProvider</c> implementation.
///     Bind from the <c>Ordering:Email</c> section.
/// </summary>
public sealed class EmailProviderOptions
{
    public const string SectionName = "Ordering:Email";

    /// <summary>
    ///     One of <c>InMemory</c> (dev/test only) or <c>SendGrid</c>.
    /// </summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    ///     SendGrid API key (Bearer token for v3/mail/send).
    /// </summary>
    public string SendGridApiKey { get; set; } = string.Empty;

    /// <summary>
    ///     Verified sender address used with SendGrid.
    /// </summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    ///     Sender display name used with SendGrid.
    /// </summary>
    public string FromName { get; set; } = "NetCommerce";

    /// <summary>
    ///     Explicit acknowledgment that swallowed (in-memory) email delivery is
    ///     acceptable in Production-like environments. Without this, booting
    ///     Production with the InMemory provider fails fast.
    /// </summary>
    public bool AllowInMemoryInProduction { get; set; } = false;
}
