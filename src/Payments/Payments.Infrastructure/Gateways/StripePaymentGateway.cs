#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.SharedKernel.Results;
using Stripe;

namespace NetCommerce.Payments.Infrastructure.Gateways;

public class StripePaymentGateway : IPaymentGateway
{
    private readonly ILogger<StripePaymentGateway> _logger;
    private readonly StripeOptions _options;
    private readonly PaymentIntentService _paymentIntentService;
    private readonly RefundService _refundService;

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

    public PaymentProvider Provider => PaymentProvider.Stripe;

    public async Task<Result<PaymentResult>> ProcessPaymentAsync(PaymentRequest request,
        CancellationToken cancellationToken = default)
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
                PaymentMethod = request.PaymentMethodToken,
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
            if (!string.IsNullOrEmpty(request.IdempotencyKey)) requestOptions.IdempotencyKey = request.IdempotencyKey;

            var paymentIntent =
                await _paymentIntentService.CreateAsync(createOptions, requestOptions, cancellationToken);

            var status = MapStripeStatus(paymentIntent.Status);
            var result = new PaymentResult(
                paymentIntent.Id,
                status,
                status == PaymentResultStatus.Failed ? paymentIntent.LastPaymentError?.Message : null);

            _logger.LogInformation(
                "Stripe payment {PaymentIntentId} completed with status {Status}",
                paymentIntent.Id, paymentIntent.Status);

            return Result.Success(result);
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

            var refund = await _refundService.CreateAsync(refundOptions, cancellationToken: cancellationToken);

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

public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
