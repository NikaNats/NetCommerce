using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Gateways;
using SharedKernel.Domain;
using SharedKernel.Results;
using Stripe;

namespace Payments.Infrastructure.Gateways;

public class StripePaymentGateway : IPaymentGateway
{
    private readonly PaymentIntentService _paymentIntentService;
    private readonly RefundService _refundService;
    private readonly ILogger<StripePaymentGateway> _logger;
    private readonly StripeOptions _options;

    public string ProviderName => "Stripe";

    public StripePaymentGateway(
        IOptions<StripeOptions> options,
        ILogger<StripePaymentGateway> logger)
    {
        _options = options.Value;
        _logger = logger;

        StripeConfiguration.ApiKey = _options.SecretKey;
        _paymentIntentService = new PaymentIntentService();
        _refundService = new RefundService();
    }

    public async Task<Result<PaymentResult>> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Processing Stripe payment for Order {OrderId}, Amount: {Amount} {Currency}",
                request.OrderId, request.Amount.Amount, request.Amount.Currency);

            var createOptions = new PaymentIntentCreateOptions
            {
                Amount = (long)(request.Amount.Amount * 100), // Stripe uses cents
                Currency = request.Amount.Currency.ToLower(),
                PaymentMethod = request.PaymentMethodId,
                Confirm = true,
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
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
            {
                requestOptions.IdempotencyKey = request.IdempotencyKey;
            }

            var paymentIntent = await _paymentIntentService.CreateAsync(createOptions, requestOptions, cancellationToken);

            var success = paymentIntent.Status == "succeeded";
            var result = new PaymentResult(
                ExternalTransactionId: paymentIntent.Id,
                Success: success,
                Status: MapStripeStatus(paymentIntent.Status),
                FailureReason: success ? null : paymentIntent.LastPaymentError?.Message,
                ProcessedAt: DateTime.UtcNow,
                Metadata: new Dictionary<string, string>
                {
                    ["stripe_status"] = paymentIntent.Status,
                    ["payment_method_type"] = paymentIntent.PaymentMethodTypes.FirstOrDefault() ?? "unknown"
                });

            _logger.LogInformation(
                "Stripe payment {PaymentIntentId} completed with status {Status}",
                paymentIntent.Id, paymentIntent.Status);

            return Result.Success(result);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment failed for Order {OrderId}: {Message}", request.OrderId, ex.Message);

            return Result.Success(new PaymentResult(
                ExternalTransactionId: null,
                Success: false,
                Status: "failed",
                FailureReason: ex.Message,
                ProcessedAt: DateTime.UtcNow,
                Metadata: new Dictionary<string, string>
                {
                    ["stripe_error_code"] = ex.StripeError?.Code ?? "unknown",
                    ["stripe_error_type"] = ex.StripeError?.Type ?? "unknown"
                }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing Stripe payment for Order {OrderId}", request.OrderId);
            return Result.Failure<PaymentResult>(Error.Failure("Payment.ProcessingError", ex.Message));
        }
    }

    public async Task<Result<RefundResult>> ProcessRefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Processing Stripe refund for Transaction {TransactionId}, Amount: {Amount}",
                request.OriginalTransactionId, request.Amount?.Amount);

            var refundOptions = new RefundCreateOptions
            {
                PaymentIntent = request.OriginalTransactionId,
                Reason = MapRefundReason(request.Reason)
            };

            if (request.Amount != null)
            {
                refundOptions.Amount = (long)(request.Amount.Amount * 100);
            }

            var requestOptions = new RequestOptions();
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
            {
                requestOptions.IdempotencyKey = request.IdempotencyKey;
            }

            var refund = await _refundService.CreateAsync(refundOptions, requestOptions, cancellationToken);

            var success = refund.Status == "succeeded";
            var result = new RefundResult(
                ExternalRefundId: refund.Id,
                Success: success,
                Status: refund.Status,
                FailureReason: success ? null : refund.FailureReason,
                RefundedAt: DateTime.UtcNow);

            _logger.LogInformation(
                "Stripe refund {RefundId} completed with status {Status}",
                refund.Id, refund.Status);

            return Result.Success(result);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund failed for Transaction {TransactionId}: {Message}",
                request.OriginalTransactionId, ex.Message);

            return Result.Success(new RefundResult(
                ExternalRefundId: null,
                Success: false,
                Status: "failed",
                FailureReason: ex.Message,
                RefundedAt: null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing Stripe refund for Transaction {TransactionId}",
                request.OriginalTransactionId);
            return Result.Failure<RefundResult>(Error.Failure("Refund.ProcessingError", ex.Message));
        }
    }

    public Task<Result<PaymentStatusResult>> GetPaymentStatusAsync(string externalTransactionId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private static string MapStripeStatus(string stripeStatus) => stripeStatus switch
    {
        "succeeded" => "completed",
        "processing" => "pending",
        "requires_action" => "requires_action",
        "requires_payment_method" => "failed",
        "canceled" => "cancelled",
        _ => stripeStatus
    };

    private static string MapRefundReason(string? reason) => reason?.ToLower() switch
    {
        "duplicate" => "duplicate",
        "fraudulent" => "fraudulent",
        _ => "requested_by_customer"
    };
}

public class StripeOptions
{
    public const string SectionName = "Stripe";
    
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
