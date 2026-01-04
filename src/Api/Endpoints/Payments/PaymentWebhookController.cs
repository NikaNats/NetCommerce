using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Microsoft.Extensions.Options;
using Wolverine;
using NetCommerce.Payments.Infrastructure.Gateways;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Api.Endpoints.Payments;

/// <summary>
/// Stripe webhook endpoint for asynchronous payment confirmation.
///
/// WEBHOOK-FIRST PATTERN (2025 Gold Standard)
/// - ProcessPaymentAsync returns "Pending" (not "Succeeded")
/// - THIS endpoint receives actual payment confirmation from Stripe
/// - Webhook signature verification prevents tampering
/// - Idempotent handling prevents duplicate processing
///
/// Prevents "Ghost Charge" vulnerability where customer is charged but order is lost.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class PaymentWebhookController : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly ILogger<PaymentWebhookController> _logger;
    private readonly StripeOptions _stripeOptions;

    public PaymentWebhookController(
        IMessageBus bus,
        ILogger<PaymentWebhookController> logger,
        IOptions<StripeOptions> stripeOptions)
    {
        _bus = bus;
        _logger = logger;
        _stripeOptions = stripeOptions.Value;
    }

    /// <summary>
    /// Handle Stripe webhook events for payment confirmation.
    ///
    /// Security:
    /// - Signature verification (prevents tampering)
    /// - IP whitelisting (TODO: optional hardening)
    /// - Rate limiting (TODO: prevent DoS)
    ///
    /// Events handled:
    /// - payment_intent.succeeded → Payment completed successfully
    /// - payment_intent.payment_failed → Payment declined by bank
    ///
    /// Flow:
    /// 1. Verify webhook signature (anti-tamper)
    /// 2. Parse event payload
    /// 3. Dispatch ProcessExternalPaymentConfirmation command
    /// 4. Return 200 OK immediately (Stripe retries on 4xx/5xx)
    /// </summary>
    [HttpPost("stripe")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleStripeWebhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("Stripe webhook received without signature header");
            return BadRequest("Missing signature");
        }

        try
        {
            // CRITICAL: Verify signature to prevent tampering
            // If signature invalid, StripeException thrown
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                signatureHeader,
                _stripeOptions.WebhookSecret,
                throwOnApiVersionMismatch: false // Allow version tolerance
            );

            _logger.LogInformation(
                "Received Stripe webhook: {EventType}, EventId: {EventId}, PaymentIntent: {PaymentIntentId}",
                stripeEvent.Type,
                stripeEvent.Id,
                (stripeEvent.Data.Object as PaymentIntent)?.Id ?? "N/A");

            // Handle payment_intent.succeeded
            if (stripeEvent.Type == "payment_intent.succeeded")
            {
                var intent = (PaymentIntent)stripeEvent.Data.Object;

                _logger.LogInformation(
                    "Payment succeeded for PaymentIntent {PaymentIntentId}, Amount: {Amount} {Currency}",
                    intent.Id,
                    intent.Amount / 100.0m,
                    intent.Currency.ToUpper());

                // Dispatch command via Wolverine (transactional outbox ensures exactly-once)
                await _bus.InvokeAsync(new ProcessExternalPaymentConfirmation(
                    ExternalTransactionId: intent.Id,
                    Status: "Succeeded",
                    WebhookEventId: stripeEvent.Id
                ));
            }
            // Handle payment_intent.payment_failed
            else if (stripeEvent.Type == "payment_intent.payment_failed")
            {
                var intent = (PaymentIntent)stripeEvent.Data.Object;
                var errorMessage = intent.LastPaymentError?.Message ?? "Unknown error";

                _logger.LogWarning(
                    "Payment failed for PaymentIntent {PaymentIntentId}: {ErrorMessage}",
                    intent.Id,
                    errorMessage);

                await _bus.InvokeAsync(new ProcessExternalPaymentConfirmation(
                    ExternalTransactionId: intent.Id,
                    Status: "Failed",
                    WebhookEventId: stripeEvent.Id
                ));
            }
            // Handle payment_intent.canceled
            else if (stripeEvent.Type == "payment_intent.canceled")
            {
                var intent = (PaymentIntent)stripeEvent.Data.Object;

                _logger.LogInformation(
                    "Payment canceled for PaymentIntent {PaymentIntentId}",
                    intent.Id);

                await _bus.InvokeAsync(new ProcessExternalPaymentConfirmation(
                    ExternalTransactionId: intent.Id,
                    Status: "Canceled",
                    WebhookEventId: stripeEvent.Id
                ));
            }
            else
            {
                // Log but ignore other event types
                _logger.LogDebug(
                    "Ignoring Stripe webhook event type: {EventType}",
                    stripeEvent.Type);
            }

            // Always return 200 OK (Stripe retries if we return 4xx/5xx)
            return Ok();
        }
        catch (StripeException ex)
        {
            // Signature verification failed or invalid payload
            _logger.LogError(ex, "Invalid Stripe webhook signature or payload");
            return BadRequest("Invalid signature or payload");
        }
        catch (Exception ex)
        {
            // Unexpected error - log but return 200 to prevent Stripe retries
            // We'll catch this via reconciliation job
            _logger.LogError(ex, "Error processing Stripe webhook. Event will be reconciled.");
            return Ok(); // Return 200 to prevent infinite retries
        }
    }
}
