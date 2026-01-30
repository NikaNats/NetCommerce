namespace NetCommerce.Kernel.Application.Notifications;

/// <summary>
///     Provider-agnostic email sending abstraction.
///     Implementations can use SendGrid, Postmark, AWS SES, or in-memory for testing.
///     Following 2025 best practices: switching providers should be a config change, not a code rewrite.
/// </summary>
public interface IEmailProvider
{
    /// <summary>
    ///     Sends an email asynchronously.
    /// </summary>
    /// <param name="to">Recipient email address</param>
    /// <param name="subject">Email subject line</param>
    /// <param name="htmlBody">HTML-formatted email body</param>
    /// <param name="cancellationToken">Cancellation token for resilience</param>
    /// <returns>Task representing the async operation</returns>
    Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
