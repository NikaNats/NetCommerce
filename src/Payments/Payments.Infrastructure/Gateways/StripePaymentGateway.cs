#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Stripe;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Kernel.Core.Results;
using Polly;
using Polly.CircuitBreaker;
using Stripe;

namespace NetCommerce.Payments.Infrastructure.Gateways;

public class StripePaymentGateway : IPaymentGateway
{
    private readonly ILogger<StripePaymentGateway> _logger;
    private readonly StripeOptions _options;
    private readonly PaymentIntentService _paymentIntentService;
    private readonly RefundService _refundService;
    private readonly ResiliencePipeline _pipeline;

    public StripePaymentGateway(
        IOptions<StripeOptions> options,
        ILogger<StripePaymentGateway> logger,
        ResiliencePipeline pipeline)
    {
        _options = options.Value;
        _logger = logger;
        _pipeline = pipeline;

        StripeConfiguration.ApiKey = _options.SecretKey;
        _paymentIntentService = new PaymentIntentService();
        _refundService = new RefundService();
    }

    public PaymentProvider Provider => PaymentProvider.Stripe;

    /// <summary>
    /// WEBHOOK-FIRST PATTERN (2025 Gold Standard)
    ///
    /// This method initiates payment but ALWAYS returns Pending status.
    /// We never trust the API response as final truth - we wait for webhook confirmation.
    ///
    /// This prevents "Ghost Charge" vulnerability where customer is charged but order is lost
    /// due to server crash after Stripe API returns success but before saving transaction ID.
    ///
    /// Payment confirmation flow:
    /// 1. Create PaymentIntent with Confirm=true (charge customer)
    /// 2. Return Pending status (even if Stripe says "succeeded")
    /// 3. Stripe sends webhook: payment_intent.succeeded
    /// 4. Webhook handler verifies signature, dispatches ProcessExternalPaymentConfirmation
    /// 5. PaymentTransaction marked as Completed
    /// 6. Saga receives PaymentCompletedDomainEvent and continues
    /// </summary>
    public async Task<Result<PaymentResult>> ProcessPaymentAsync(PaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Initiating Stripe payment for Order {OrderId}, Amount: {Amount} {Currency}. " +
                "Returning Pending status - awaiting webhook confirmation.",
                request.OrderId, request.Amount.Amount, request.Amount.Currency);

            var createOptions = new PaymentIntentCreateOptions
            {
                Amount = (long)(request.Amount.Amount * 100), // Stripe uses cents
                Currency = request.Amount.Currency.ToLower(),
                PaymentMethod = request.PaymentMethodToken,
                Confirm = true, // Attempt immediate charge
                CaptureMethod = "automatic", // Auto-capture if succeeded
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                },
                Metadata = new Dictionary<string, string>
                {
                    ["order_id"] = request.OrderId.ToString(),
                    ["idempotency_key"] = request.IdempotencyKey ?? ""
                }
            };

            var requestOptions = new RequestOptions();
            if (!string.IsNullOrEmpty(request.IdempotencyKey)) requestOptions.IdempotencyKey = request.IdempotencyKey;

            var paymentIntent = await _pipeline.ExecuteAsync(
                async ct => await _paymentIntentService.CreateAsync(createOptions, requestOptions, ct),
                cancellationToken);

            _logger.LogInformation(
                "Stripe PaymentIntent {PaymentIntentId} created with status {Status}. " +
                "Returning Pending - awaiting webhook confirmation for safety.",
                paymentIntent.Id, paymentIntent.Status);

            // CRITICAL: Always return Pending, even if Stripe says "succeeded"
            // We wait for webhook confirmation to prevent Ghost Charge vulnerability
            var result = new PaymentResult(
                paymentIntent.Id,
                PaymentResultStatus.Pending,
                null);

            return Result.Success(result);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "Stripe circuit breaker is OPEN for Order {OrderId}. Fast-failing payment.",
                request.OrderId);
            return Result.Failure<PaymentResult>(Error.Failure(
                "Payment.CircuitOpen",
                "Payment service is temporarily unavailable. Please try again in a moment."));
        }
        catch (StripeException ex) when (ex.IsCardDeclined())
        {
            _logger.LogInformation(
                "Card declined for Order {OrderId}: {UserMessage}",
                request.OrderId, ex.GetUserMessage());
            // Card decline is a domain outcome, not an infrastructure failure
            return Result.Success(new PaymentResult(
                string.Empty,
                PaymentResultStatus.Failed,
                ex.GetUserMessage()));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment failed for Order {OrderId}: {Message}", request.OrderId, ex.Message);
            return Result.Success(new PaymentResult(
                string.Empty,
                PaymentResultStatus.Failed,
                ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing Stripe payment for Order {OrderId}", request.OrderId);
            return Result.Failure<PaymentResult>(Error.Failure("Payment.ProcessingError", ex.Message));
        }
    }

    /// <summary>
    /// Query current payment status from Stripe API.
    /// Used by PaymentReconciliationJob to catch missed/delayed webhooks.
    /// </summary>
    public async Task<Result<PaymentResult>> GetPaymentStatusAsync(
        string externalTransactionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Querying Stripe payment status for PaymentIntent {PaymentIntentId}",
                externalTransactionId);

            var paymentIntent = await _pipeline.ExecuteAsync(
                async ct => await _paymentIntentService.GetAsync(
                    externalTransactionId,
                    cancellationToken: ct),
                cancellationToken);

            var status = MapStripeStatus(paymentIntent.Status);
            var result = new PaymentResult(
                paymentIntent.Id,
                status,
                status == PaymentResultStatus.Failed ? paymentIntent.LastPaymentError?.Message : null);

            _logger.LogInformation(
                "Stripe PaymentIntent {PaymentIntentId} has status {Status}",
                paymentIntent.Id, paymentIntent.Status);

            return Result.Success(result);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "Stripe circuit breaker is OPEN. Fast-failing status query for {PaymentIntentId}.",
                externalTransactionId);
            return Result.Failure<PaymentResult>(Error.Failure(
                "Payment.CircuitOpen",
                "Payment service is temporarily unavailable."));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to query Stripe payment status for {PaymentIntentId}: {Message}",
                externalTransactionId, ex.Message);
            return Result.Failure<PaymentResult>(Error.Failure("Payment.StatusQueryError", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error querying Stripe payment status for {PaymentIntentId}",
                externalTransactionId);
            return Result.Failure<PaymentResult>(Error.Failure("Payment.StatusQueryError", ex.Message));
        }
    }

    public async Task<Result<RefundResult>> ProcessRefundAsync(RefundRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Processing Stripe refund for Transaction {TransactionId}, Amount: {Amount}",
                request.OriginalTransactionId, request.Amount.Amount);

            var refundOptions = new RefundCreateOptions
            {
                PaymentIntent = request.OriginalTransactionId,
                Amount = (long)(request.Amount.Amount * 100),
                Reason = MapRefundReason(request.Reason)
            };

            var refund = await _pipeline.ExecuteAsync(
                async ct => await _refundService.CreateAsync(refundOptions, cancellationToken: ct),
                cancellationToken);

            var success = refund.Status == "succeeded";
            var result = new RefundResult(
                refund.Id,
                success,
                success ? null : refund.FailureReason);

            _logger.LogInformation(
                "Stripe refund {RefundId} completed with status {Status}",
                refund.Id, refund.Status);

            return Result.Success(result);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "Stripe circuit breaker is OPEN. Fast-failing refund for Transaction {TransactionId}.",
                request.OriginalTransactionId);
            return Result.Failure<RefundResult>(Error.Failure(
                "Refund.CircuitOpen",
                "Refund service is temporarily unavailable. The operation will be retried automatically."));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund failed for Transaction {TransactionId}: {Message}",
                request.OriginalTransactionId, ex.Message);
            return Result.Success(new RefundResult(
                string.Empty,
                false,
                ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing Stripe refund for Transaction {TransactionId}",
                request.OriginalTransactionId);
            return Result.Failure<RefundResult>(Error.Failure("Refund.ProcessingError", ex.Message));
        }
    }

    private static PaymentResultStatus MapStripeStatus(string stripeStatus)
    {
        return stripeStatus switch
        {
            "succeeded" => PaymentResultStatus.Succeeded,
            "processing" => PaymentResultStatus.Pending,
            "requires_action" => PaymentResultStatus.RequiresAction,
            "requires_payment_method" => PaymentResultStatus.Failed,
            "canceled" => PaymentResultStatus.Failed,
            _ => PaymentResultStatus.Pending
        };
    }

    private static string MapRefundReason(string? reason)
    {
        return reason?.ToLower() switch
        {
            "duplicate" => "duplicate",
            "fraudulent" => "fraudulent",
            _ => "requested_by_customer"
        };
    }
}
