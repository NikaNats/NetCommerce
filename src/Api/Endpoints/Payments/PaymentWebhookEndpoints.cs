#region

using System.Diagnostics;
using Asp.Versioning.Builder;
using Microsoft.Extensions.Options;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Kernel.Stripe;
using Stripe;
using Wolverine;

#endregion

namespace NetCommerce.Api.Endpoints.Payments;

/// <summary>
///     Stripe webhook endpoint for asynchronous payment confirmation.
///     WEBHOOK-FIRST PATTERN (2025 Gold Standard)
///     - ProcessPaymentAsync returns "Pending" (not "Succeeded")
///     - THIS endpoint receives actual payment confirmation from Stripe
///     - Webhook signature verification prevents tampering
///     - Idempotent handling prevents duplicate processing
///     Prevents "Ghost Charge" vulnerability where customer is charged but order is lost.
///
///     NATIVE AOT MIGRATION (Phase 2):
///     - Converted from MVC Controller to Minimal API
///     - Static handler method avoids closure allocations
///     - Direct dependency injection via parameters (AOT-friendly)
///     - Eliminates Microsoft.AspNetCore.Mvc dependency chain
/// </summary>
public class PaymentWebhookEndpoints : IEndpoint
{
    /// <summary>
    ///     Map webhook endpoint. Does NOT use versioning since Stripe calls a fixed URL.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        // Webhooks are authenticated via signature, not JWT tokens
        // Antiforgery is disabled because this is machine-to-machine communication
        app.MapPost("/api/webhooks/stripe", HandleStripeWebhook)
           .WithName("StripeWebhook")
           .WithDisplayName("Stripe Payment Webhook")
           .WithDescription("Receives asynchronous payment confirmation from Stripe")
           .AllowAnonymous()
           .DisableAntiforgery()
           .WithOpenApi(); // Include in Scalar/Swagger for documentation
    }

    /// <summary>
    ///     Handle Stripe webhook events for payment confirmation.
    ///
    ///     Security:
    ///     - Signature verification (prevents tampering)
    ///     - IP whitelisting (TODO: optional hardening)
    ///     - Rate limiting (TODO: prevent DoS)
    ///
    ///     Events handled:
    ///     - payment_intent.succeeded → Payment completed successfully
    ///     - payment_intent.payment_failed → Payment declined by bank
    ///     - payment_intent.canceled → Payment canceled by user or system
    ///
    ///     Flow:
    ///     1. Verify webhook signature (anti-tamper)
    ///     2. Parse event payload
    ///     3. Dispatch ProcessExternalPaymentConfirmation command
    ///     4. Return 200 OK immediately (Stripe retries on 4xx/5xx)
    ///
    ///     Static handler is AOT-friendly:
    ///     - No closure allocations
    ///     - Dependencies injected by Minimal API binder at compile-time
    ///     - Trim-safe (no reflection in user code)
    /// </summary>
    private static async Task<IResult> HandleStripeWebhook(
        HttpRequest request,
        IMessageBus bus,
        IOptions<StripeOptions> options,
        ILogger<PaymentWebhookEndpoints> logger)
    {
        var stripeOptions = options.Value;

        // 1. Read the raw body stream for signature verification
        // CRITICAL: Stripe requires the exact raw JSON for signature validation
        string json;
        using (var reader = new StreamReader(request.Body))
        {
            json = await reader.ReadToEndAsync();
        }

        // 2. Get the signature header
        string signatureHeader = request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrEmpty(signatureHeader))
        {
            logger.LogWarning("Stripe webhook received without signature header");
            return Results.BadRequest("Missing signature");
        }

        try
        {
            // 3. Verify signature (Anti-Tamper)
            // CRITICAL: Verify signature to prevent tampering
            // If signature invalid, StripeException is thrown
            // Note: Stripe.NET uses some reflection internally (EventUtility.ConstructEvent)
            // We tolerate this library usage because:
            // - It's isolated to webhook verification
            // - The reflection is within the library's control
            // - Alternative would require reimplementing Stripe's security logic
            Event? stripeEvent = EventUtility.ConstructEvent(
                json,
                signatureHeader,
                stripeOptions.WebhookSecret,
                throwOnApiVersionMismatch: false // Allow version tolerance
            );

            logger.LogInformation(
                "Received Stripe webhook: {EventType}, EventId: {EventId}, PaymentIntent: {PaymentIntentId}",
                stripeEvent.Type,
                stripeEvent.Id,
                (stripeEvent.Data.Object as PaymentIntent)?.Id ?? "N/A");

            // 4. Map Event to Wolverine Command using pattern matching
            // This replaces the if-else chain with a more maintainable switch expression
            object? commandToDispatch = stripeEvent.Type switch
            {
                "payment_intent.succeeded" => CreateSuccessCommand(stripeEvent, logger),
                "payment_intent.payment_failed" => CreateFailureCommand(stripeEvent, logger),
                "payment_intent.canceled" => CreateCancellationCommand(stripeEvent, logger),
                _ => LogUnhandledEvent(stripeEvent.Type, logger)
            };

            // 5. Dispatch if matched
            if (commandToDispatch != null)
            {
                // Wolverine Transactional Outbox ensures exactly-once delivery
                // Even if this process crashes after this call, the command will be delivered
                await bus.InvokeAsync(commandToDispatch);
                logger.LogInformation("Dispatched confirmation for event type: {Type}", stripeEvent.Type);
            }

            // Always return 200 OK to Stripe if signature was valid
            // Returning 4xx/5xx causes Stripe to retry (up to 72 hours)
            return Results.Ok();
        }
        catch (StripeException ex)
        {
            // Signature verification failed or invalid payload
            logger.LogError(ex, "Invalid Stripe webhook signature or payload");
            return Results.BadRequest("Invalid signature or payload");
        }
        catch (Exception ex)
        {
            // CRITICAL: Return 500 to signal Stripe to retry
            // Stripe has exponential backoff (up to 72 hours of retries)
            // Returning 200 on error causes permanent data loss!
            logger.LogError(ex,
                "Error processing Stripe webhook. Returning 500 to trigger Stripe retry. " +
                "Reconciliation job will also catch this. CorrelationId: {CorrelationId}",
                Activity.Current?.Id ?? "N/A");

            // Return 500 to trigger Stripe's retry mechanism
            return Results.Problem(
                title: "Internal processing error",
                detail: "Webhook will be retried by Stripe",
                statusCode: 500,
                type: "https://netcommerce.example.com/errors/webhook-processing"
            );
        }
    }

    // ============================================================================
    // Command Factory Methods (Extract for maintainability)
    // ============================================================================

    private static ProcessExternalPaymentConfirmation CreateSuccessCommand(Event stripeEvent, ILogger logger)
    {
        var intent = (PaymentIntent)stripeEvent.Data.Object;

        logger.LogInformation(
            "Payment succeeded for PaymentIntent {PaymentIntentId}, Amount: {Amount} {Currency}",
            intent.Id,
            intent.Amount / 100.0m,
            intent.Currency.ToUpper());

        return new ProcessExternalPaymentConfirmation(
            intent.Id,
            "Succeeded",
            stripeEvent.Id
        );
    }

    private static ProcessExternalPaymentConfirmation CreateFailureCommand(Event stripeEvent, ILogger logger)
    {
        var intent = (PaymentIntent)stripeEvent.Data.Object;
        string errorMessage = intent.LastPaymentError?.Message ?? "Unknown error";

        logger.LogWarning(
            "Payment failed for PaymentIntent {PaymentIntentId}: {ErrorMessage}",
            intent.Id,
            errorMessage);

        return new ProcessExternalPaymentConfirmation(
            intent.Id,
            "Failed",
            stripeEvent.Id
        );
    }

    private static ProcessExternalPaymentConfirmation CreateCancellationCommand(Event stripeEvent, ILogger logger)
    {
        var intent = (PaymentIntent)stripeEvent.Data.Object;

        logger.LogInformation(
            "Payment canceled for PaymentIntent {PaymentIntentId}",
            intent.Id);

        return new ProcessExternalPaymentConfirmation(
            intent.Id,
            "Canceled",
            stripeEvent.Id
        );
    }

    private static object? LogUnhandledEvent(string eventType, ILogger logger)
    {
        // Log but ignore other event types (no command to dispatch)
        logger.LogDebug("Ignoring Stripe webhook event type: {EventType}", eventType);
        return null;
    }
}
