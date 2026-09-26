#nullable enable
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Application.Notifications;

namespace NetCommerce.Ordering.Infrastructure.Notifications;

/// <summary>
///     SendGrid v3 email provider (<c>POST /v3/mail/send</c>) for production delivery.
///     Throws on transport or API failures so Wolverine retries (order notifications)
///     or per-channel catch blocks (financial alerts) engage.
/// </summary>
public sealed class SendGridEmailProvider : IEmailProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailProviderOptions _options;
    private readonly ILogger<SendGridEmailProvider> _logger;

    public SendGridEmailProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<EmailProviderOptions> options,
        ILogger<SendGridEmailProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient email is required.", nameof(to));

        var client = _httpClientFactory.CreateClient("SendGrid");

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.SendGridApiKey);
        request.Content = JsonContent.Create(new
        {
            personalizations = new[]
            {
                new { to = new[] { new { email = to } } }
            },
            from = new { email = _options.FromEmail, name = _options.FromName },
            subject,
            content = new[]
            {
                new { type = "text/html", value = htmlBody }
            }
        });

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "SendGrid rejected email to {To} (subject: {Subject}). Status: {StatusCode}, Body: {Body}",
                to, subject, response.StatusCode, body);

            throw new InvalidOperationException(
                $"SendGrid email send failed with status {(int)response.StatusCode} ({response.StatusCode}).");
        }

        _logger.LogInformation("Order email sent via SendGrid to {To} (subject: {Subject})", to, subject);
    }
}
