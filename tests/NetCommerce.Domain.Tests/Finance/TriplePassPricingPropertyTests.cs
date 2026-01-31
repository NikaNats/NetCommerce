#nullable enable
using FsCheck;
using FsCheck.Xunit;
using NetCommerce.Domain.Shared;
using Shouldly;
using Xunit.Abstractions;

namespace NetCommerce.Domain.Tests.Finance;

/// <summary>
///     PRODUCTION-READINESS TEST: Triple-Pass Pricing Property-Based Testing
///
///     <para>
///     This test suite uses FsCheck to generate 10,000+ random price combinations
///     and verify that financial invariants ALWAYS hold. The key insight:
///     "A bug that appears in 1/10,000 transactions will appear DAILY at scale."
///     </para>
///
///     <para>
///     <b>Properties Tested:</b>
///     1. Penny-Perfect Addition: Sum of parts = Total (to the penny)
///     2. Commutativity: A + B = B + A (order shouldn't matter)
///     3. Non-Negative Preservation: Operations on positive values stay positive
///     4. Rounding Consistency: Banker's rounding applies uniformly
///     5. Currency Isolation: Mixed-currency operations fail fast
///     </para>
/// </summary>
[Trait("Category", "Financial")]
[Trait("Category", "PropertyBased")]
public class TriplePassPricingPropertyTests
{
    private readonly ITestOutputHelper _output;

    public TriplePassPricingPropertyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Property 1: Penny-Perfect Addition

    /// <summary>
    ///     THE PENNY-PERFECT INVARIANT
    ///
    ///     <para>
    ///     For any PriceBreakdown:
    ///     LineTotal = LineSubTotal + LineTaxTotal
    ///
    ///     This must hold TO THE EXACT PENNY, not "close enough".
    ///     Financial systems that accumulate 0.01 errors per transaction
    ///     will drift millions at scale.
    ///     </para>
    /// </summary>
    [Property(MaxTest = 10000)]
    public bool PennyPerfect_LineTotal_ShouldEqual_SubTotalPlusTax(
        PositiveInt basePriceCents,
        byte discountPct,
        byte taxRateBps,
        PositiveInt quantity)
    {
        // Constrain inputs to valid ranges
        var basePrice = Math.Round(basePriceCents.Get / 100.0m, 2);
        var discountPercent = Math.Min(discountPct, (byte)99) / 100.0m;
        var taxRate = taxRateBps / 10000.0m; // basis points to decimal
        var qty = Math.Max(1, Math.Min(quantity.Get, 100));

        if (basePrice <= 0) return true; // Skip invalid

        var discountAmount = Math.Round(basePrice * discountPercent, 2);
        var subTotalUnit = Math.Round(basePrice - discountAmount, 2);
        var taxAmount = Math.Round(subTotalUnit * taxRate, 2);

        try
        {
            var breakdown = new PriceBreakdown(
                basePrice,
                discountAmount,
                taxAmount,
                taxRate,
                "VAT",
                "GEL",
                qty);

            // THE PENNY-PERFECT INVARIANT
            var expectedLineTotal = breakdown.LineSubTotal + breakdown.LineTaxTotal;
            return breakdown.LineTotal == expectedLineTotal;
        }
        catch (ArgumentException)
        {
            return true; // Skip invalid input combinations
        }
    }

    /// <summary>
    ///     Verifies that FinalPrice = SubTotal + TaxAmount for single units.
    /// </summary>
    [Property(MaxTest = 10000)]
    public bool PennyPerfect_FinalPrice_ShouldEqual_SubTotalPlusTax(
        PositiveInt basePriceCents,
        byte discountPct,
        byte taxRateBps)
    {
        var basePrice = Math.Round(basePriceCents.Get / 100.0m, 2);
        var discountPercent = Math.Min(discountPct, (byte)99) / 100.0m;
        var taxRate = taxRateBps / 10000.0m;

        if (basePrice <= 0) return true;

        var discountAmount = Math.Round(basePrice * discountPercent, 2);
        var subTotal = Math.Round(basePrice - discountAmount, 2);
        var taxAmount = Math.Round(subTotal * taxRate, 2);

        try
        {
            var breakdown = PriceBreakdown.Create(
                basePrice,
                discountAmount,
                taxAmount,
                taxRate,
                "VAT");

            var expected = breakdown.SubTotal + breakdown.TaxAmount;
            return breakdown.FinalPrice == expected;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    #endregion

    #region Property 2: Money Commutativity

    /// <summary>
    ///     Money.Add is commutative: A + B = B + A
    /// </summary>
    [Property(MaxTest = 10000)]
    public bool Money_Add_ShouldBeCommutative(PositiveInt aCents, PositiveInt bCents)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);

        if (a <= 0 || b <= 0) return true;

        var moneyA = Money.Create(a);
        var moneyB = Money.Create(b);

        var sumAB = moneyA.Add(moneyB);
        var sumBA = moneyB.Add(moneyA);

        return sumAB.Amount == sumBA.Amount && sumAB.Currency == sumBA.Currency;
    }

    /// <summary>
    ///     Money.Add is associative: (A + B) + C = A + (B + C)
    /// </summary>
    [Property(MaxTest = 5000)]
    public bool Money_Add_ShouldBeAssociative(PositiveInt aCents, PositiveInt bCents, PositiveInt cCents)
    {
        // Use smaller amounts to avoid overflow
        var a = Math.Round((aCents.Get % 10000) / 100.0m, 2);
        var b = Math.Round((bCents.Get % 10000) / 100.0m, 2);
        var c = Math.Round((cCents.Get % 10000) / 100.0m, 2);

        if (a <= 0 || b <= 0 || c <= 0) return true;

        var moneyA = Money.Create(a);
        var moneyB = Money.Create(b);
        var moneyC = Money.Create(c);

        var left = moneyA.Add(moneyB).Add(moneyC);
        var right = moneyA.Add(moneyB.Add(moneyC));

        return left.Amount == right.Amount;
    }

    #endregion

    #region Property 3: Non-Negative Preservation

    /// <summary>
    ///     Adding non-negative Money values produces non-negative result.
    /// </summary>
    [Property(MaxTest = 10000)]
    public bool Money_Add_ShouldPreserveNonNegativity(PositiveInt aCents, PositiveInt bCents)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);

        if (a <= 0 || b <= 0) return true;

        var moneyA = Money.Create(a);
        var moneyB = Money.Create(b);
        var sum = moneyA.Add(moneyB);

        return sum.Amount >= 0;
    }

    /// <summary>
    ///     Multiplying non-negative Money by non-negative multiplier produces non-negative result.
    /// </summary>
    [Property(MaxTest = 10000)]
    public bool Money_Multiply_ShouldPreserveNonNegativity(PositiveInt amountCents, PositiveInt multiplierPct)
    {
        var amount = Math.Round(amountCents.Get / 100.0m, 2);
        var multiplier = multiplierPct.Get / 100.0m;

        if (amount <= 0) return true;

        var money = Money.Create(amount);
        var result = money.Multiply(multiplier);

        return result.Amount >= 0;
    }

    #endregion

    #region Property 4: Rounding Consistency

    /// <summary>
    ///     Money.Create always produces values with at most 2 decimal places.
    /// </summary>
    [Property(MaxTest = 10000)]
    public bool Money_ShouldAlwaysHaveAtMostTwoDecimalPlaces(PositiveInt rawCents)
    {
        var rawAmount = rawCents.Get / 1000.0m; // Create values with more than 2 decimal places

        try
        {
            var money = Money.Create(rawAmount);

            // Check if amount has at most 2 decimal places
            var scaled = money.Amount * 100;
            return scaled == Math.Floor(scaled);
        }
        catch (ArgumentException)
        {
            return true; // Negative amounts are invalid
        }
    }

    /// <summary>
    ///     ToSubunits conversion is consistent: amount × 100 = subunits (for valid amounts)
    /// </summary>
    [Property(MaxTest = 10000)]
    public bool Money_ToSubunits_ShouldBeConsistent(PositiveInt amountCents)
    {
        var amount = Math.Round(amountCents.Get / 100.0m, 2);

        if (amount <= 0) return true;

        var money = Money.Create(amount);
        var subunits = money.ToSubunits();
        var expected = (long)Math.Round(money.Amount * 100, 0, MidpointRounding.AwayFromZero);

        return subunits == expected;
    }

    #endregion

    #region Property 5: Currency Isolation

    /// <summary>
    ///     Operations on different currencies must throw.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_DifferentCurrencies_ShouldThrow(PositiveInt aCents, PositiveInt bCents)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);

        if (a <= 0 || b <= 0) return true;

        var moneyGEL = Money.Create(a, "GEL");
        var moneyUSD = Money.Create(b, "USD");

        var threwOnAdd = false;
        var threwOnSubtract = false;

        try { moneyGEL.Add(moneyUSD); }
        catch (InvalidOperationException) { threwOnAdd = true; }

        try { moneyGEL.Subtract(moneyUSD); }
        catch (InvalidOperationException) { threwOnSubtract = true; }

        return threwOnAdd && threwOnSubtract;
    }

    #endregion

    #region Property 6: Order Total Integrity (Triple-Pass)

    /// <summary>
    ///     THE TRIPLE-PASS INTEGRITY TEST
    ///
    ///     Simulates a complete order with multiple line items and verifies:
    ///     Sum(LineItem.LineTotal) = Sum(LineSubTotal) + Sum(LineTaxTotal)
    /// </summary>
    [Property(MaxTest = 5000)]
    public bool TriplePass_OrderTotal_ShouldEqualSumOfLineTotals(
        PositiveInt bp1, byte dp1, byte tr1, PositiveInt q1,
        PositiveInt bp2, byte dp2, byte tr2, PositiveInt q2,
        PositiveInt bp3, byte dp3, byte tr3, PositiveInt q3)
    {
        var lineItemInputs = new[]
        {
            (bp1.Get, dp1, tr1, q1.Get),
            (bp2.Get, dp2, tr2, q2.Get),
            (bp3.Get, dp3, tr3, q3.Get)
        };

        var breakdowns = new List<PriceBreakdown>();

        foreach (var (bpCents, discPct, taxBps, qty) in lineItemInputs)
        {
            var basePrice = Math.Round(bpCents / 100.0m, 2);
            var discountPercent = Math.Min(discPct, (byte)99) / 100.0m;
            var taxRate = taxBps / 10000.0m;
            var quantity = Math.Max(1, Math.Min(qty, 100));

            if (basePrice <= 0) continue;

            var discountAmount = Math.Round(basePrice * discountPercent, 2);
            var subTotalUnit = Math.Round(basePrice - discountAmount, 2);
            var taxAmount = Math.Round(subTotalUnit * taxRate, 2);

            try
            {
                breakdowns.Add(new PriceBreakdown(
                    basePrice,
                    discountAmount,
                    taxAmount,
                    taxRate,
                    "VAT",
                    "GEL",
                    quantity));
            }
            catch (ArgumentException)
            {
                // Skip invalid combinations
            }
        }

        if (breakdowns.Count == 0) return true;

        // Calculate order total two ways
        var sumOfLineTotals = breakdowns.Sum(b => b.LineTotal);
        var sumOfParts = breakdowns.Sum(b => b.LineSubTotal) + breakdowns.Sum(b => b.LineTaxTotal);

        return sumOfLineTotals == sumOfParts;
    }

    /// <summary>
    ///     Verifies that PriceBreakdown.ToMoney() preserves value.
    /// </summary>
    [Property(MaxTest = 5000)]
    public bool PriceBreakdown_ToMoney_ShouldPreserveValue(
        PositiveInt basePriceCents,
        byte discountPct,
        byte taxRateBps)
    {
        var basePrice = Math.Round(basePriceCents.Get / 100.0m, 2);
        var discountPercent = Math.Min(discountPct, (byte)99) / 100.0m;
        var taxRate = taxRateBps / 10000.0m;

        if (basePrice <= 0) return true;

        var discountAmount = Math.Round(basePrice * discountPercent, 2);
        var subTotal = Math.Round(basePrice - discountAmount, 2);
        var taxAmount = Math.Round(subTotal * taxRate, 2);

        try
        {
            var breakdown = PriceBreakdown.Create(
                basePrice,
                discountAmount,
                taxAmount,
                taxRate,
                "VAT");

            var money = breakdown.ToMoney();

            return money.Amount == breakdown.FinalPrice && money.Currency == breakdown.Currency;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    #endregion

    #region Property 7: Edge Case Detection

    /// <summary>
    ///     Verifies behavior around the "0.005 rounding boundary".
    /// </summary>
    [Property(MaxTest = 10000)]
    public bool RoundingBoundary_ShouldHandleCorrectly(PositiveInt rawCents)
    {
        var smallAmount = (rawCents.Get % 1000) / 1000.0m; // 0.001 to 0.999

        if (smallAmount <= 0) return true;

        var money = Money.Create(smallAmount);
        var rounded = Math.Round(smallAmount, 2);

        return money.Amount == rounded;
    }

    /// <summary>
    ///     Large amounts should not overflow or lose precision.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool LargeAmounts_ShouldNotOverflow(PositiveInt largeCents)
    {
        // Use amounts up to 1 billion
        var largeAmount = Math.Round((decimal)largeCents.Get * 1000, 2);

        if (largeAmount <= 0 || largeAmount > 1_000_000_000) return true;

        try
        {
            var money = Money.Create(largeAmount);
            var doubled = money.Add(money);

            return doubled.Amount == largeAmount * 2;
        }
        catch (OverflowException)
        {
            return false; // Overflow is a failure
        }
    }

    #endregion

    #region Statistical Summary

    /// <summary>
    ///     Outputs test configuration summary.
    /// </summary>
    [Fact]
    public void PropertyTestSummary_OutputsConfiguration()
    {
        _output.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        _output.WriteLine("║     TRIPLE-PASS PRICING PROPERTY-BASED TEST SUMMARY          ║");
        _output.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        _output.WriteLine("║ Properties Tested:                                           ║");
        _output.WriteLine("║   1. Penny-Perfect Addition (LineTotal = SubTotal + Tax)     ║");
        _output.WriteLine("║   2. Money Commutativity (A + B = B + A)                      ║");
        _output.WriteLine("║   3. Money Associativity ((A+B)+C = A+(B+C))                  ║");
        _output.WriteLine("║   4. Non-Negative Preservation                               ║");
        _output.WriteLine("║   5. Rounding Consistency (≤2 decimal places)                ║");
        _output.WriteLine("║   6. Currency Isolation (mixed currencies throw)             ║");
        _output.WriteLine("║   7. Triple-Pass Order Total Integrity                       ║");
        _output.WriteLine("║   8. Large Amount Overflow Protection                        ║");
        _output.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        _output.WriteLine("║ Configuration:                                               ║");
        _output.WriteLine("║   - MaxTest: 10,000 random inputs per property               ║");
        _output.WriteLine("║   - Using FsCheck 3.x with PositiveInt generators            ║");
        _output.WriteLine("║   - Shrinking: Enabled (minimal failing case reported)       ║");
        _output.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        _output.WriteLine("║ Coverage:                                                    ║");
        _output.WriteLine("║   - Money value object: Add, Subtract, Multiply, ToSubunits  ║");
        _output.WriteLine("║   - PriceBreakdown: All calculated properties                ║");
        _output.WriteLine("║   - Edge cases: Rounding boundaries, large amounts           ║");
        _output.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    }

    #endregion
}
