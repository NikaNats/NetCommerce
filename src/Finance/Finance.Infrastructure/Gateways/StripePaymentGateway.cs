using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Kernel.Stripe;
using Stripe;

namespace NetCommerce.Finance.Infrastructure.Gateways;

/// <summary>
///     Stripe Payment Gateway implementation for reconciliation.
///     Fetches payout data and transaction details from Stripe API.
///     Uses shared StripeClientFactory from NetCommerce.Kernel.Stripe.
/// </summary>
public class StripeReconciliationGateway : IPaymentGateway
{
    private readonly StripeClientFactory _stripeFactory;
    private readonly ILogger<StripeReconciliationGateway> _logger;

    public StripeReconciliationGateway(StripeClientFactory stripeFactory, ILogger<StripeReconciliationGateway> logger)
    {
        _stripeFactory = stripeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExternalTransaction>> GetExternalLedgerAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching Stripe transactions for {Date}", date.ToShortDateString());

            var startOfDay = new DateTimeOffset(date.Date, TimeSpan.Zero);
            var endOfDay = new DateTimeOffset(date.Date.AddDays(1), TimeSpan.Zero);

            var balanceTransactionService = _stripeFactory.CreateBalanceTransactionService();
            var options = new BalanceTransactionListOptions
            {
                Created = new DateRangeOptions
                {
                    GreaterThanOrEqual = startOfDay.UtcDateTime,
                    LessThan = endOfDay.UtcDateTime
                },
                Type = "charge", // Only successful charges
                Limit = 100
            };

            var transactions = new List<ExternalTransaction>();
            var stripeTransactions = await balanceTransactionService.ListAsync(options, cancellationToken: cancellationToken);

            foreach (var tx in stripeTransactions)
            {
                transactions.Add(new ExternalTransaction(
                    tx.Id,
                    tx.Amount / 100m, // Convert from cents
                    tx.Net / 100m,    // Net after fees
                    tx.Fee / 100m,
                    tx.Currency.ToUpper(),
                    tx.Created,
                    tx.Description));
            }

            _logger.LogInformation("Fetched {Count} transactions from Stripe for {Date}", transactions.Count, date.ToShortDateString());
            return transactions;
        }
        catch (StripeException ex) when (ex.IsTransient())
        {
            _logger.LogWarning(ex, "Transient Stripe error fetching ledger for {Date}, retrying...", date);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch external ledger from Stripe for {Date}", date);
            throw;
        }
    }

    public async Task<ExternalTransaction?> GetTransactionDetailsAsync(
        string externalTransactionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching Stripe transaction details for {TransactionId}", externalTransactionId);

            var balanceTransactionService = _stripeFactory.CreateBalanceTransactionService();
            var tx = await balanceTransactionService.GetAsync(externalTransactionId, cancellationToken: cancellationToken);

            if (tx == null)
            {
                _logger.LogWarning("Transaction {Id} not found in Stripe", externalTransactionId);
                return null;
            }

            return new ExternalTransaction(
                tx.Id,
                tx.Amount / 100m,
                tx.Net / 100m,
                tx.Fee / 100m,
                tx.Currency.ToUpper(),
                tx.Created,
                tx.Description);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Transaction {Id} not found in Stripe", externalTransactionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch transaction details for {TransactionId}", externalTransactionId);
            throw;
        }
    }

    public async Task<string> RefundTransactionAsync(
        string externalTransactionId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogCritical(
                "RECONCILIATION REFUND: Processing refund for ghost charge {TransactionId}, Amount: {Amount}, Reason: {Reason}",
                externalTransactionId, amount, reason);

            var refundService = _stripeFactory.CreateRefundService();
            var options = new RefundCreateOptions
            {
                Charge = externalTransactionId,
                Amount = (long)(amount * 100), // Convert to cents
                Reason = RefundReasons.Duplicate, // Ghost charges are duplicates
                Metadata = new Dictionary<string, string>
                {
                    ["reconciliation_reason"] = reason,
                    ["source"] = "netcommerce_reconciliation"
                }
            };

            var refund = await refundService.CreateAsync(options, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Refund {RefundId} created for ghost charge {TransactionId}",
                refund.Id, externalTransactionId);

            return refund.Id;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "Failed to refund transaction {TransactionId}: {Message}",
                externalTransactionId, ex.GetUserMessage());
            throw;
        }
    }
}
