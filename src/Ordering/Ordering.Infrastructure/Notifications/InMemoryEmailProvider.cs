#region

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Application.Notifications;

#endregion

namespace NetCommerce.Ordering.Infrastructure.Notifications;

/// <summary>
///     In-memory email provider for development and testing.
///     Stores sent emails in memory for inspection instead of actually sending them.
///     Production systems should use SendGridEmailProvider or AwsSesEmailProvider.
/// </summary>
public class InMemoryEmailProvider : IEmailProvider
{
    private readonly ILogger<InMemoryEmailProvider> _logger;
    private readonly ConcurrentBag<SentEmail> _sentEmails = new();

    public InMemoryEmailProvider(ILogger<InMemoryEmailProvider> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string htmlBody,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[IN-MEMORY EMAIL] To: {To}, Subject: {Subject}",
            to, subject);

        var email = new SentEmail(
            to,
            subject,
            htmlBody,
            DateTimeOffset.UtcNow);

        _sentEmails.Add(email);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Gets all sent emails for testing/inspection.
    /// </summary>
    public IReadOnlyCollection<SentEmail> GetSentEmails()
    {
        return _sentEmails.ToList();
    }

    /// <summary>
    ///     Clears all sent emails (useful for test cleanup).
    /// </summary>
    public void Clear()
    {
        _sentEmails.Clear();
    }
}

/// <summary>
///     Record of an email sent via the in-memory provider.
/// </summary>
public sealed record SentEmail(
    string To,
    string Subject,
    string HtmlBody,
    DateTimeOffset SentAt);
