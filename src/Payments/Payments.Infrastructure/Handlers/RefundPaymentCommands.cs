using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Results;
using Wolverine.Attributes;

namespace NetCommerce.Payments.Infrastructure.Handlers;

/// <summary>
///     Wolverine handler for RefundPaymentTransactionCommand.
/// </summary>
[WolverineHandler]
public static class RefundPaymentTransactionHandler
{
    public static async Task<Result> HandleAsync(
        RefundPaymentTransactionCommand command,
        PaymentsDbContext db,
        IPaymentGateway paymentGateway,
        ILogger<RefundPaymentTransactionCommand> logger,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.ExternalTransactionId == command.PaymentTransactionId, cancellationToken);

        if (transaction is null)
            return Result.Failure(Error.NotFound("PaymentTransaction", command.PaymentTransactionId));

        // Idempotency: if already refunded, treat as success.
        if (transaction.Status == PaymentStatus.Refunded)
        {
            logger.LogInformation(
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
            command.Amount,
            command.Reason);

        var refundResult = await paymentGateway.ProcessRefundAsync(refundRequest, cancellationToken);

        if (!refundResult.IsSuccess)
            return Result.Failure(refundResult.Error!);

        if (!refundResult.Value.Success)
            return Result.Failure(Error.Failure(
                "Payment.RefundFailed",
                refundResult.Value.ErrorMessage ?? "Refund failed"));

        transaction.MarkAsRefunded(refundResult.Value.RefundId);

        logger.LogInformation(
            "PaymentTransaction {TransactionId} refunded. RefundId: {RefundId}",
            transaction.Id, refundResult.Value.RefundId);

        return Result.Success();
    }
}
