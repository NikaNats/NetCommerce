using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Domain.Gateways;

namespace NetCommerce.Finance.Infrastructure.Gateways;

/// <summary>
///     Stripe Payment Gateway implementation for reconciliation.
///     Fetches payout data and transaction details from Stripe API.
/// </summary>
public class StripePaymentGateway : IPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(HttpClient httpClient, ILogger<StripePaymentGateway> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Configure HTTP client for Stripe API
        _httpClient.BaseAddress = new Uri("https://api.stripe.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetApiKey()}");
    }

    public async Task<IReadOnlyList<ExternalTransaction>> GetExternalLedgerAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching Stripe transactions for {Date}", date.ToShortDateString());

            // In a real implementation, this would call Stripe's Balance Transactions API
            // or Payouts API to get settled transactions for the date
            // For demo purposes, returning mock data

            var startOfDay = new DateTimeOffset(date.Date).ToUnixTimeSeconds();
            var endOfDay = new DateTimeOffset(date.Date.AddDays(1)).ToUnixTimeSeconds();

            // Mock implementation - replace with actual Stripe API calls
            var transactions = await GetStripeBalanceTransactionsAsync(startOfDay, endOfDay, cancellationToken);

            return transactions;
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

            // Call Stripe Balance Transaction API
            var response = await _httpClient.GetAsync($"balance_transactions/{externalTransactionId}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch transaction {Id}: {Status}", externalTransactionId, response.StatusCode);
                return null;
            }

            // Parse response and return ExternalTransaction
            // Implementation would parse the JSON response

            return null; // Placeholder
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
            _logger.LogWarning("Processing refund for ghost charge {TransactionId}, Amount: {Amount}, Reason: {Reason}",
                externalTransactionId, amount, reason);

            // Call Stripe Refund API
            var refundData = new
            {
                charge = externalTransactionId,
                amount = (long)(amount * 100), // Convert to cents
                reason = "duplicate", // or "fraudulent" based on context
                metadata = new { reconciliation_reason = reason }
            };

            // Implementation would POST to /refunds endpoint

            return "mock_refund_id"; // Placeholder
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refund transaction {TransactionId}", externalTransactionId);
            throw;
        }
    }

    private async Task<IReadOnlyList<ExternalTransaction>> GetStripeBalanceTransactionsAsync(
        long startTimestamp,
        long endTimestamp,
        CancellationToken cancellationToken)
    {
        // Mock implementation - in reality, this would:
        // 1. Call Stripe Balance Transactions API with date filter
        // 2. Filter for successful charges (type: 'charge')
        // 3. Calculate net amounts (gross - fees)
        // 4. Return ExternalTransaction records

        var mockTransactions = new List<ExternalTransaction>
        {
            new ExternalTransaction(
                "ch_mock_123",
                99.99m,
                97.49m, // Net after 2.5% + 30¢ fee
                2.50m,
                "USD",
                DateTime.UtcNow.AddHours(-2),
                "Mock transaction for reconciliation testing")
        };

        return mockTransactions;
    }

    private string GetApiKey()
    {
        // In production, get from configuration/secrets
        return Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? "sk_test_mock_key";
    }
}
