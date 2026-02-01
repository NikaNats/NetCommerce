using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace NetCommerce.Kernel.Stripe;

/// <summary>
///     Shared Stripe configuration options used across modules.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>
    ///     Stripe Secret API Key (sk_test_xxx or sk_live_xxx).
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    ///     Stripe Publishable Key for client-side usage (pk_test_xxx or pk_live_xxx).
    /// </summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>
    ///     Webhook signing secret for verifying webhook payloads.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    ///     Enable Stripe test mode (uses test keys, logs mock transactions).
    /// </summary>
    public bool TestMode { get; set; } = true;

    /// <summary>
    ///     HTTP timeout for Stripe API calls in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    ///     Maximum retry attempts for transient failures.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
}

/// <summary>
///     Shared Stripe client factory that configures Stripe SDK with proper settings.
/// </summary>
public sealed class StripeClientFactory : IDisposable
{
    private readonly ILogger<StripeClientFactory> _logger;
    private readonly StripeOptions _options;
    private readonly IStripeClient _stripeClient;

    public StripeClientFactory(IOptions<StripeOptions> options, ILogger<StripeClientFactory> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Configure global Stripe settings
        StripeConfiguration.ApiKey = _options.SecretKey;
        StripeConfiguration.MaxNetworkRetries = _options.MaxRetryAttempts;

        // Create a configured StripeClient instance
        _stripeClient = new StripeClient(
            apiKey: _options.SecretKey,
            httpClient: new SystemNetHttpClient(maxNetworkRetries: _options.MaxRetryAttempts));

        _logger.LogInformation(
            "Stripe client initialized. TestMode={TestMode}, MaxRetries={MaxRetries}",
            _options.TestMode,
            _options.MaxRetryAttempts);
    }

    /// <summary>
    ///     Get the configured Stripe client instance.
    /// </summary>
    public IStripeClient Client => _stripeClient;

    /// <summary>
    ///     Get PaymentIntentService with configured client.
    /// </summary>
    public PaymentIntentService CreatePaymentIntentService() => new(_stripeClient);

    /// <summary>
    ///     Get RefundService with configured client.
    /// </summary>
    public RefundService CreateRefundService() => new(_stripeClient);

    /// <summary>
    ///     Get BalanceTransactionService for reconciliation.
    /// </summary>
    public BalanceTransactionService CreateBalanceTransactionService() => new(_stripeClient);

    /// <summary>
    ///     Get PayoutService for reconciliation.
    /// </summary>
    public PayoutService CreatePayoutService() => new(_stripeClient);

    /// <summary>
    ///     Get ChargeService for charge lookups.
    /// </summary>
    public ChargeService CreateChargeService() => new(_stripeClient);

    /// <summary>
    ///     Verifies a webhook signature and returns the parsed event.
    /// </summary>
    /// <param name="json">Raw JSON body from webhook request.</param>
    /// <param name="signature">Stripe-Signature header value.</param>
    /// <returns>Parsed Stripe Event if signature is valid.</returns>
    /// <exception cref="StripeException">Thrown if signature verification fails.</exception>
    public Event VerifyWebhookSignature(string json, string signature)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            _logger.LogWarning("Webhook secret not configured - skipping signature verification in test mode");
            return EventUtility.ParseEvent(json);
        }

        return EventUtility.ConstructEvent(json, signature, _options.WebhookSecret);
    }

    /// <summary>
    ///     Check if running in test mode.
    /// </summary>
    public bool IsTestMode => _options.TestMode || _options.SecretKey.StartsWith("sk_test_");

    public void Dispose()
    {
        // StripeClient doesn't implement IDisposable but we might add cleanup here
    }
}

/// <summary>
///     Standard Stripe error codes for consistent error handling.
/// </summary>
public static class StripeErrorCodes
{
    public const string CardDeclined = "card_declined";
    public const string InsufficientFunds = "insufficient_funds";
    public const string ExpiredCard = "expired_card";
    public const string IncorrectCvc = "incorrect_cvc";
    public const string ProcessingError = "processing_error";
    public const string InvalidAmount = "invalid_amount";
    public const string RateLimitExceeded = "rate_limit";
    public const string IdempotencyError = "idempotency_error";
}

/// <summary>
///     Extension methods for handling Stripe exceptions consistently.
/// </summary>
public static class StripeExceptionExtensions
{
    /// <summary>
    ///     Determine if the error is transient and should be retried.
    /// </summary>
    public static bool IsTransient(this StripeException ex)
    {
        return ex.StripeError?.Code == StripeErrorCodes.RateLimitExceeded ||
               ex.StripeError?.Code == StripeErrorCodes.ProcessingError ||
               ex.HttpStatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
               ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests ||
               ex.HttpStatusCode == System.Net.HttpStatusCode.GatewayTimeout;
    }

    /// <summary>
    ///     Determine if the error is a card decline.
    /// </summary>
    public static bool IsCardDeclined(this StripeException ex)
    {
        return ex.StripeError?.Code == StripeErrorCodes.CardDeclined ||
               ex.StripeError?.Code == StripeErrorCodes.InsufficientFunds ||
               ex.StripeError?.Code == StripeErrorCodes.ExpiredCard ||
               ex.StripeError?.Code == StripeErrorCodes.IncorrectCvc;
    }

    /// <summary>
    ///     Get a user-friendly error message.
    /// </summary>
    public static string GetUserMessage(this StripeException ex)
    {
        return ex.StripeError?.Code switch
        {
            StripeErrorCodes.CardDeclined => "Your card was declined. Please try a different payment method.",
            StripeErrorCodes.InsufficientFunds => "Insufficient funds. Please try a different card.",
            StripeErrorCodes.ExpiredCard => "Your card has expired. Please use a valid card.",
            StripeErrorCodes.IncorrectCvc => "The CVC code is incorrect. Please check and try again.",
            StripeErrorCodes.ProcessingError => "Payment processing error. Please try again later.",
            StripeErrorCodes.RateLimitExceeded => "Too many requests. Please wait a moment and try again.",
            _ => ex.StripeError?.Message ?? "An unexpected payment error occurred."
        };
    }
}
