using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Payments.Application.Transactions.Commands;

/// <summary>
///     Refund a previously completed payment transaction.
///     Used as a compensating action when downstream steps (e.g., inventory confirmation) fail.
/// </summary>
public sealed record RefundPaymentTransactionCommand(
    Guid PaymentTransactionId,
    Money Amount,
    string Reason) : ICommand;

public sealed class RefundPaymentTransactionCommandHandler : ICommandHandler<RefundPaymentTransactionCommand>
{
    private readonly ILogger<RefundPaymentTransactionCommandHandler> _logger;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IPaymentTransactionRepository _transactionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RefundPaymentTransactionCommandHandler(
        IPaymentTransactionRepository transactionRepository,
        IPaymentGateway paymentGateway,
        IUnitOfWork unitOfWork,
        ILogger<RefundPaymentTransactionCommandHandler> logger)
    {
        _transactionRepository = transactionRepository;
        _paymentGateway = paymentGateway;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(RefundPaymentTransactionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdAsync(request.PaymentTransactionId, cancellationToken);

        if (transaction is null)
            return Result.Failure(Error.NotFound("PaymentTransaction", request.PaymentTransactionId));

        // Idempotency: if already refunded, treat as success.
        if (transaction.Status == PaymentStatus.Refunded)
        {
            _logger.LogInformation(
                "PaymentTransaction {TransactionId} already refunded; skipping",
                transaction.Id);

            return Result.Success();
        }

        if (transaction.Status != PaymentStatus.Completed)
            return Result.Failure(Error.Conflict(
                $"Cannot refund payment in status {transaction.Status}"));

        if (string.IsNullOrWhiteSpace(transaction.ExternalTransactionId))
            return Result.Failure(Error.Failure(
                "Payment.RefundMissingExternalId",
                "Payment transaction is missing external transaction id"));

        var refundRequest = new RefundRequest(
            transaction.ExternalTransactionId,
            request.Amount,
            request.Reason);

        var refundResult = await _paymentGateway.ProcessRefundAsync(refundRequest, cancellationToken);

        if (!refundResult.IsSuccess) return Result.Failure(refundResult.Error!);

        if (!refundResult.Value.Success)
            return Result.Failure(Error.Failure(
                "Payment.RefundFailed",
                refundResult.Value.ErrorMessage ?? "Refund failed"));

        transaction.MarkAsRefunded(refundResult.Value.RefundId);
        _transactionRepository.Update(transaction);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}