using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Payments.Application.Gateways;

/// <summary>
///     Payment gateway abstraction for multiple providers.
/// </summary>
public interface IPaymentGateway
{
    PaymentProvider Provider { get; }

    Task<Result<PaymentResult>> ProcessPaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<RefundResult>> ProcessRefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default);
}

public record PaymentRequest(
    Guid OrderId,
    Money Amount,
    string PaymentMethodToken,
    string IdempotencyKey,
    string? Description = null,
    Dictionary<string, string>? Metadata = null);

public record PaymentResult(
    string TransactionId,
    PaymentResultStatus Status,
    string? ErrorMessage = null);

public record RefundRequest(
    string OriginalTransactionId,
    Money Amount,
    string Reason);

public record RefundResult(
    string RefundId,
    bool Success,
    string? ErrorMessage = null);

public enum PaymentResultStatus
{
    Succeeded,
    Pending,
    Failed,
    RequiresAction
}

public enum PaymentProvider
{
    Stripe,
    BankOfGeorgia,
    TBC
}