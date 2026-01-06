using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Payments.Application.Transactions.Commands;

/// <summary>
///     Refund a previously completed payment transaction.
///     Used as a compensating action when downstream steps (e.g., inventory confirmation) fail.
/// </summary>
public sealed record RefundPaymentTransactionCommand(
    string PaymentTransactionId,
    Money Amount,
    string Reason) : ICommand;
