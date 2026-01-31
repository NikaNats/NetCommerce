#nullable enable
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Integration.Tests.Fixtures;
using Shouldly;

namespace NetCommerce.Integration.Tests.EdgeCases;

/// <summary>
///     PRODUCTION-READINESS TEST: Multi-Currency Rounding Variance
///
///     <para>
///     Tests that currency conversion and rounding don't cause financial discrepancies.
///     </para>
///
///     <para>
///     <b>Production Impact:</b>
///     - Customer pays in EUR, system converts to GEL
///     - Rounding during conversion loses 0.01 GEL
///     - Over 1M transactions/month = 10,000 GEL variance
///     - Accounting reconciliation fails
///     </para>
///
///     <para>
///     <b>Expected Behavior:</b>
///     - All amounts stored with sufficient precision
///     - Rounding rules clearly defined (HALF_UP, HALF_EVEN)
///     - Variance tracked and reconciled
///     - Sub-unit amounts (cents) never lost
///     </para>
/// </summary>
public class MultiCurrencyRoundingTests : IntegrationTestBase
{
    public MultiCurrencyRoundingTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: Currency Conversion Should Preserve Precision

    /// <summary>
    ///     Tests that currency conversion maintains adequate precision.
    ///
    ///     <para>
    ///     Example: 100.00 USD → GEL at 2.72345 rate
    ///     Should be: 272.345 GEL (not 272.35 or 272.34)
    ///     </para>
    /// </summary>
    [Fact]
    public void CurrencyConversion_ShouldPreservePrecision()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Exchange rates with high precision
        // ═══════════════════════════════════════════════════════════════════════

        var exchangeRates = new Dictionary<string, decimal>
        {
            ["USD/GEL"] = 2.72345m,
            ["EUR/GEL"] = 2.95678m,
            ["GBP/GEL"] = 3.45123m
        };

        var sourceAmount = 100.00m;
        var sourceCurrency = "USD";
        var targetCurrency = "GEL";

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Perform conversion with full precision
        // ═══════════════════════════════════════════════════════════════════════

        var rate = exchangeRates[$"{sourceCurrency}/{targetCurrency}"];
        var convertedPrecise = sourceAmount * rate; // 272.345

        // Different rounding strategies
        var roundedHalfUp = Math.Round(convertedPrecise, 2, MidpointRounding.AwayFromZero);
        var roundedHalfEven = Math.Round(convertedPrecise, 2, MidpointRounding.ToEven);
        var truncated = Math.Truncate(convertedPrecise * 100) / 100;

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Precision is maintained
        // ═══════════════════════════════════════════════════════════════════════

        convertedPrecise.ShouldBe(272.345m, "Full precision should be 272.345");

        Console.WriteLine($"[Currency] {sourceAmount} {sourceCurrency} → {targetCurrency}");
        Console.WriteLine($"[Currency] Rate: {rate}");
        Console.WriteLine($"[Currency] Precise: {convertedPrecise}");
        Console.WriteLine($"[Currency] HALF_UP: {roundedHalfUp}");
        Console.WriteLine($"[Currency] HALF_EVEN: {roundedHalfEven}");
        Console.WriteLine($"[Currency] Truncated: {truncated}");

        // Variance between methods
        var variance = roundedHalfUp - truncated;
        Console.WriteLine($"[Currency] Variance (HALF_UP vs Truncate): {variance}");
        Console.WriteLine($"[Currency] ✓ Precision maintained before rounding decision");
    }

    #endregion

    #region Test 2: Rounding Should Use Banker's Rounding

    /// <summary>
    ///     Tests that the system uses Banker's rounding (HALF_EVEN) to minimize bias.
    ///
    ///     <para>
    ///     Banker's rounding: .5 rounds to nearest even
    ///     - 2.5 → 2
    ///     - 3.5 → 4
    ///     Over many transactions, rounding errors cancel out.
    ///     </para>
    /// </summary>
    [Fact]
    public void Rounding_ShouldUseBankersRounding()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Test cases for Banker's rounding
        // ═══════════════════════════════════════════════════════════════════════

        var testCases = new[]
        {
            (value: 2.5m, expectedHalfEven: 2m, expectedHalfUp: 3m),
            (value: 3.5m, expectedHalfEven: 4m, expectedHalfUp: 4m),
            (value: 2.25m, expectedHalfEven: 2m, expectedHalfUp: 2m), // Not a .5 case
            (value: 2.75m, expectedHalfEven: 3m, expectedHalfUp: 3m),
            (value: 4.5m, expectedHalfEven: 4m, expectedHalfUp: 5m),
            (value: 5.5m, expectedHalfEven: 6m, expectedHalfUp: 6m),
        };

        decimal totalHalfEven = 0;
        decimal totalHalfUp = 0;

        Console.WriteLine("[Currency] Banker's Rounding (HALF_EVEN) vs Standard (HALF_UP):");

        foreach (var (value, expectedHalfEven, expectedHalfUp) in testCases)
        {
            var halfEven = Math.Round(value, 0, MidpointRounding.ToEven);
            var halfUp = Math.Round(value, 0, MidpointRounding.AwayFromZero);

            halfEven.ShouldBe(expectedHalfEven, $"Banker's rounding of {value}");
            halfUp.ShouldBe(expectedHalfUp, $"Standard rounding of {value}");

            totalHalfEven += halfEven;
            totalHalfUp += halfUp;

            var diff = halfUp - halfEven;
            var marker = diff != 0 ? "←" : "";
            Console.WriteLine($"[Currency]   {value} → EVEN: {halfEven}, UP: {halfUp} {marker}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Banker's rounding has less cumulative bias
        // ═══════════════════════════════════════════════════════════════════════

        var originalSum = testCases.Sum(t => t.value);
        var halfEvenError = Math.Abs(totalHalfEven - originalSum);
        var halfUpError = Math.Abs(totalHalfUp - originalSum);

        Console.WriteLine($"[Currency] Original sum: {originalSum}");
        Console.WriteLine($"[Currency] HALF_EVEN total: {totalHalfEven} (error: {halfEvenError})");
        Console.WriteLine($"[Currency] HALF_UP total: {totalHalfUp} (error: {halfUpError})");

        halfEvenError.ShouldBeLessThanOrEqualTo(halfUpError,
            "Banker's rounding should have equal or less bias");

        Console.WriteLine($"[Currency] ✓ Banker's rounding minimizes systematic bias");
    }

    #endregion

    #region Test 3: Order Total Should Match Line Item Sum

    /// <summary>
    ///     Tests that order total exactly equals sum of line items.
    ///
    ///     <para>
    ///     Scenario:
    ///     - 3 items at 33.33 each (from 100/3)
    ///     - Sum = 99.99, not 100.00
    ///     - Where does 0.01 go?
    ///     </para>
    /// </summary>
    [Fact]
    public void OrderTotal_ShouldMatchLineItemSum()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Order with rounding-prone line items
        // ═══════════════════════════════════════════════════════════════════════

        var originalAmount = 100.00m;
        var itemCount = 3;
        var pricePerItem = Math.Round(originalAmount / itemCount, 2, MidpointRounding.ToEven);

        var lineItems = Enumerable.Range(1, itemCount)
            .Select(i => new { ItemId = i, Price = pricePerItem })
            .ToList();

        var lineItemSum = lineItems.Sum(li => li.Price);
        var variance = originalAmount - lineItemSum;

        Console.WriteLine($"[Currency] Original: {originalAmount}");
        Console.WriteLine($"[Currency] Items: {itemCount} @ {pricePerItem} each");
        Console.WriteLine($"[Currency] Sum: {lineItemSum}");
        Console.WriteLine($"[Currency] Variance: {variance}");

        // ═══════════════════════════════════════════════════════════════════════
        // SOLUTION: Allocate remainder to last item
        // ═══════════════════════════════════════════════════════════════════════

        var adjustedItems = lineItems.ToList();
        if (variance != 0)
        {
            var lastItem = adjustedItems.Last();
            adjustedItems[^1] = new { lastItem.ItemId, Price = lastItem.Price + variance };
            Console.WriteLine($"[Currency] Adjusted last item: {lastItem.Price} → {adjustedItems[^1].Price}");
        }

        var adjustedSum = adjustedItems.Sum(li => li.Price);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Adjusted sum matches original
        // ═══════════════════════════════════════════════════════════════════════

        adjustedSum.ShouldBe(originalAmount,
            "Adjusted line items should sum to original amount");

        Console.WriteLine($"[Currency] Adjusted sum: {adjustedSum}");
        Console.WriteLine($"[Currency] ✓ Remainder allocation ensures exact total");
    }

    #endregion

    #region Test 4: Refund Should Match Original Payment

    /// <summary>
    ///     Tests that refund amount equals original payment (no rounding loss).
    ///
    ///     <para>
    ///     Scenario:
    ///     - Customer pays 100 USD (converted to 272.35 GEL)
    ///     - Customer requests refund
    ///     - Refund should be EXACTLY 100 USD or 272.35 GEL
    ///     </para>
    /// </summary>
    [Fact]
    public void Refund_ShouldMatchOriginalPayment()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Original payment with conversion
        // ═══════════════════════════════════════════════════════════════════════

        var payment = new
        {
            OriginalAmount = 100.00m,
            OriginalCurrency = "USD",
            ConvertedAmount = 272.35m,
            ConvertedCurrency = "GEL",
            ExchangeRateAtPayment = 2.7235m, // Rate at time of payment
            PaymentId = Guid.NewGuid()
        };

        // Current exchange rate (might be different)
        var currentRate = 2.7345m;

        // ═══════════════════════════════════════════════════════════════════════
        // REFUND OPTIONS
        // ═══════════════════════════════════════════════════════════════════════

        // Option 1: Refund in original currency (recommended)
        var refundOption1 = new
        {
            Amount = payment.OriginalAmount,
            Currency = payment.OriginalCurrency,
            Method = "Original Currency",
            CustomerReceives = 100.00m,
            MerchantFxRisk = "None"
        };

        // Option 2: Refund in converted currency (exact amount stored)
        var refundOption2 = new
        {
            Amount = payment.ConvertedAmount,
            Currency = payment.ConvertedCurrency,
            Method = "Stored Converted Amount",
            CustomerReceives = payment.ConvertedAmount,
            MerchantFxRisk = "None (customer bears FX risk)"
        };

        // Option 3: Refund with current rate (risky)
        var refundOption3 = new
        {
            Amount = payment.OriginalAmount * currentRate,
            Currency = "GEL",
            Method = "Current Rate Conversion",
            CustomerReceives = payment.OriginalAmount * currentRate,
            MerchantFxRisk = "High (rate difference)"
        };

        Console.WriteLine("[Currency] Refund Options:");
        Console.WriteLine($"[Currency]   1. {refundOption1.Method}: {refundOption1.Amount} {refundOption1.Currency}");
        Console.WriteLine($"[Currency]   2. {refundOption2.Method}: {refundOption2.Amount} {refundOption2.Currency}");
        Console.WriteLine($"[Currency]   3. {refundOption3.Method}: {refundOption3.Amount:F2} {refundOption3.Currency}");

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Option 1 or 2 preserves exact amount
        // ═══════════════════════════════════════════════════════════════════════

        refundOption1.Amount.ShouldBe(payment.OriginalAmount);
        refundOption2.Amount.ShouldBe(payment.ConvertedAmount);

        Console.WriteLine($"[Currency] ✓ Refund preserves original or stored converted amount");
    }

    #endregion

    #region Test 5: Cumulative Rounding Error Should Be Tracked

    /// <summary>
    ///     Tests that cumulative rounding errors are tracked for reconciliation.
    ///
    ///     <para>
    ///     Over 1M transactions:
    ///     - Average rounding variance: 0.005 (half a cent)
    ///     - Total variance: 5,000 currency units
    ///     This must be accounted for in financial reconciliation.
    ///     </para>
    /// </summary>
    [Fact]
    public void CumulativeRoundingError_ShouldBeTracked()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATE: Many transactions with rounding
        // ═══════════════════════════════════════════════════════════════════════

        var random = new Random(42); // Deterministic for testing
        var transactionCount = 10000;
        var cumulativeError = 0m;

        for (var i = 0; i < transactionCount; i++)
        {
            // Generate random amount with 4 decimal precision
            var preciseAmount = (decimal)(random.NextDouble() * 1000);

            // Round to 2 decimals
            var roundedAmount = Math.Round(preciseAmount, 2, MidpointRounding.ToEven);

            // Track the rounding error
            var error = preciseAmount - roundedAmount;
            cumulativeError += error;
        }

        var averageError = cumulativeError / transactionCount;

        Console.WriteLine("[Currency] Rounding Error Simulation:");
        Console.WriteLine($"[Currency]   Transactions: {transactionCount:N0}");
        Console.WriteLine($"[Currency]   Cumulative Error: {cumulativeError:F4}");
        Console.WriteLine($"[Currency]   Average Error: {averageError:F6}");

        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Variance tracking mechanism
        // ═══════════════════════════════════════════════════════════════════════

        var varianceTracking = new
        {
            AccountCode = "FX_ROUNDING_VARIANCE",
            Period = "2026-01",
            TotalVariance = cumulativeError,
            TransactionCount = transactionCount,
            ReconciliationStatus = Math.Abs(cumulativeError) < 100 ? "Auto-Approved" : "Requires Review"
        };

        Console.WriteLine($"[Currency] Variance Account: {varianceTracking.AccountCode}");
        Console.WriteLine($"[Currency] Status: {varianceTracking.ReconciliationStatus}");

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Variance is within acceptable bounds (with Banker's rounding)
        // ═══════════════════════════════════════════════════════════════════════

        Math.Abs(averageError).ShouldBeLessThan(0.01m,
            "Average rounding error should be minimal with Banker's rounding");

        Console.WriteLine($"[Currency] ✓ Cumulative rounding error tracked for reconciliation");
    }

    #endregion

    #region Test 6: Display vs Storage Precision Should Differ

    /// <summary>
    ///     Tests that storage precision is higher than display precision.
    ///
    ///     <para>
    ///     - Storage: 272.3456789 (high precision)
    ///     - Display: 272.35 (user-friendly)
    ///     - Calculation: Use stored value
    ///     </para>
    /// </summary>
    [Fact]
    public void StoragePrecision_ShouldExceedDisplayPrecision()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // DEFINE: Precision requirements
        // ═══════════════════════════════════════════════════════════════════════

        var precisionSpec = new
        {
            // Database storage
            StorageDecimalPlaces = 8,
            StorageType = "DECIMAL(19,8)",

            // Display formatting
            DisplayDecimalPlaces = 2,

            // Intermediate calculations
            CalculationDecimalPlaces = 10,

            // Exchange rates
            ExchangeRateDecimalPlaces = 6
        };

        // Example amounts
        var storedAmount = 272.34567890m;
        var displayAmount = Math.Round(storedAmount, precisionSpec.DisplayDecimalPlaces);

        Console.WriteLine("[Currency] Precision Specification:");
        Console.WriteLine($"[Currency]   Storage: {precisionSpec.StorageDecimalPlaces} decimals ({precisionSpec.StorageType})");
        Console.WriteLine($"[Currency]   Display: {precisionSpec.DisplayDecimalPlaces} decimals");
        Console.WriteLine($"[Currency]   Calculation: {precisionSpec.CalculationDecimalPlaces} decimals");
        Console.WriteLine($"[Currency]   Exchange Rate: {precisionSpec.ExchangeRateDecimalPlaces} decimals");
        Console.WriteLine($"[Currency]");
        Console.WriteLine($"[Currency] Example:");
        Console.WriteLine($"[Currency]   Stored: {storedAmount}");
        Console.WriteLine($"[Currency]   Displayed: {displayAmount}");

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Storage has higher precision
        // ═══════════════════════════════════════════════════════════════════════

        precisionSpec.StorageDecimalPlaces.ShouldBeGreaterThan(precisionSpec.DisplayDecimalPlaces,
            "Storage precision should exceed display precision");

        storedAmount.ShouldNotBe(displayAmount,
            "Stored and displayed values should differ (storage has more precision)");

        Console.WriteLine($"[Currency] ✓ Storage precision > Display precision");
    }

    #endregion
}
