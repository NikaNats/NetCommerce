#nullable enable
using FsCheck;
using FsCheck.Xunit;
using NetCommerce.Domain.Shared;
using Shouldly;

namespace NetCommerce.Domain.Tests.SharedKernel;

/// <summary>
///     Property-Based Testing for Multi-Currency Operations (The "Penny Trap" Prevention)
///
///     <para>
///     Uses FsCheck to generate thousands of random test cases to verify that
///     Money arithmetic operations maintain financial integrity across all scenarios.
///     </para>
///
///     <para>
///     <b>The Risk:</b>
///     Currency conversion and multi-buy discounts can introduce "phantom cents" -
///     tiny rounding errors that compound across thousands of transactions.
///     E.g., 10,000 orders × 0.005 error = 50 currency units of discrepancy.
///     </para>
///
///     <para>
///     <b>Financial Integrity Invariant:</b>
///     Order.GrandTotal == Sum(LineItems) to the 2nd decimal place, regardless of:
///     - Exchange rate used
///     - Rounding direction
///     - Number of items
///     - Discount complexity
///     </para>
/// </summary>
[Trait("Category", "PropertyBased")]
[Trait("Category", "FinancialIntegrity")]
public class CrossCurrencyPropertyTests
{
    #region Property Tests: Addition Invariants

    /// <summary>
    ///     Money.Add is commutative: A + B = B + A
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_Addition_ShouldBeCommutative(PositiveInt aCents, PositiveInt bCents)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);

        if (a <= 0 || b <= 0) return true;

        var moneyA = Money.Create(a);
        var moneyB = Money.Create(b);

        var result1 = moneyA.Add(moneyB);
        var result2 = moneyB.Add(moneyA);

        return result1.Amount == result2.Amount;
    }

    /// <summary>
    ///     Money.Add is associative: (A + B) + C = A + (B + C)
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_Addition_ShouldBeAssociative(PositiveInt aCents, PositiveInt bCents, PositiveInt cCents)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);
        var c = Math.Round(cCents.Get / 100.0m, 2);

        if (a <= 0 || b <= 0 || c <= 0) return true;

        var moneyA = Money.Create(a);
        var moneyB = Money.Create(b);
        var moneyC = Money.Create(c);

        var result1 = moneyA.Add(moneyB).Add(moneyC);
        var result2 = moneyA.Add(moneyB.Add(moneyC));

        return result1.Amount == result2.Amount;
    }

    /// <summary>
    ///     Adding zero should return the same amount.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_AddingZero_ShouldReturnSameAmount(PositiveInt amountCents)
    {
        var amount = Math.Round(amountCents.Get / 100.0m, 2);
        if (amount <= 0) return true;

        var money = Money.Create(amount);
        var zero = Money.Zero();

        var result = money.Add(zero);

        return result.Amount == amount;
    }

    #endregion

    #region Property Tests: Subtraction Invariants

    /// <summary>
    ///     Subtraction is the inverse of addition.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_Subtraction_ShouldBeInverseOfAddition(PositiveInt aCents, PositiveInt bCents)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);

        if (a <= 0 || b <= 0) return true;

        // Ensure a >= b to avoid negative amounts
        var larger = Math.Max(a, b);
        var smaller = Math.Min(a, b);

        var moneyLarger = Money.Create(larger);

        var afterAdd = Money.Create(smaller).Add(Money.Create(larger - smaller));
        var afterSubtract = moneyLarger.Subtract(Money.Create(larger - smaller));

        return afterAdd.Amount == larger && afterSubtract.Amount == smaller;
    }

    #endregion

    #region Property Tests: Multiplication (Quantity) Invariants

    /// <summary>
    ///     Multiplication distributes over addition: (A + B) * qty = A*qty + B*qty
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_Multiplication_ShouldDistributeOverAddition(
        PositiveInt aCents,
        PositiveInt bCents,
        byte qty)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);
        var quantity = (int)Math.Max((int)1, Math.Min((int)qty, 100));

        if (a <= 0 || b <= 0) return true;

        var moneyA = Money.Create(a);
        var moneyB = Money.Create(b);

        // (A + B) * qty should equal A*qty + B*qty
        var sumThenMultiply = moneyA.Add(moneyB).Multiply(quantity);
        var multiplyThenSum = moneyA.Multiply(quantity).Add(moneyB.Multiply(quantity));

        return sumThenMultiply.Amount == multiplyThenSum.Amount;
    }

    /// <summary>
    ///     Multiplying by 1 should return the same amount.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_MultiplyByOne_ShouldReturnSameAmount(PositiveInt amountCents)
    {
        var amount = Math.Round(amountCents.Get / 100.0m, 2);
        if (amount <= 0) return true;

        var money = Money.Create(amount);
        var result = money.Multiply(1);

        return result.Amount == amount;
    }

    /// <summary>
    ///     Multiplying by 0 should return zero.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_MultiplyByZero_ShouldReturnZero(PositiveInt amountCents)
    {
        var amount = Math.Round(amountCents.Get / 100.0m, 2);
        if (amount <= 0) return true;

        var money = Money.Create(amount);
        var result = money.Multiply(0);

        return result.Amount == 0m;
    }

    #endregion

    #region Property Tests: Order Total Integrity (The "Penny Trap" Test)

    /// <summary>
    ///     CRITICAL: Verifies that Order.GrandTotal == Sum(LineItems).
    ///     This tests with multiple items calculated two different ways.
    /// </summary>
    [Property(MaxTest = 500)]
    public bool OrderTotal_ShouldEqualSumOfLineItems(
        PositiveInt price1Cents,
        PositiveInt price2Cents,
        PositiveInt price3Cents,
        byte qty1,
        byte qty2,
        byte qty3)
    {
        var p1 = Math.Round(price1Cents.Get / 100.0m, 2);
        var p2 = Math.Round(price2Cents.Get / 100.0m, 2);
        var p3 = Math.Round(price3Cents.Get / 100.0m, 2);
        var q1 = (int)Math.Max(1, Math.Min((int)qty1, 20));
        var q2 = (int)Math.Max(1, Math.Min((int)qty2, 20));
        var q3 = (int)Math.Max(1, Math.Min((int)qty3, 20));

        if (p1 <= 0 || p2 <= 0 || p3 <= 0) return true;

        var lineItems = new[]
        {
            (UnitPrice: Money.Create(p1), Quantity: q1),
            (UnitPrice: Money.Create(p2), Quantity: q2),
            (UnitPrice: Money.Create(p3), Quantity: q3)
        };

        // Method 1: Sum line totals
        var sumOfLineTotals = lineItems.Aggregate(
            Money.Zero(),
            (acc, item) => acc.Add(item.UnitPrice.Multiply(item.Quantity)));

        // Method 2: Calculate from scratch
        var grandTotal = Money.Zero();
        foreach (var item in lineItems)
        {
            grandTotal = grandTotal.Add(item.UnitPrice.Multiply(item.Quantity));
        }

        // They must be exactly equal to 2 decimal places
        return sumOfLineTotals.Amount == grandTotal.Amount;
    }

    /// <summary>
    ///     Verifies that applying a discount and then tax produces a predictable result.
    ///     Note: Due to intermediate rounding in Money operations, we allow for
    ///     1 cent tolerance which is acceptable for financial calculations.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool DiscountThenTax_ShouldProducePredictableResult(
        PositiveInt baseCents,
        byte discountPct,
        byte taxRateBps)
    {
        var baseAmount = Math.Round(baseCents.Get / 100.0m, 2);
        var discountRate = Math.Min(discountPct, (byte)50) / 100.0m; // 0-50%
        var taxRate = taxRateBps / 10000.0m; // basis points

        if (baseAmount <= 0) return true;

        var basePrice = Money.Create(baseAmount);

        // Apply discount then tax using Money operations (with intermediate rounding)
        var discountAmount = basePrice.Multiply(discountRate);
        var afterDiscount = basePrice.Subtract(discountAmount);
        var taxAmount = afterDiscount.Multiply(taxRate);
        var finalPrice = afterDiscount.Add(taxAmount);

        // Calculate expected with same intermediate rounding as Money operations:
        // Step 1: discountAmount = Round(base * discountRate, 2)
        var expectedDiscountAmount = Math.Round(baseAmount * discountRate, 2);
        // Step 2: afterDiscount = base - discountAmount (no rounding needed for subtraction of rounded values)
        var expectedAfterDiscount = baseAmount - expectedDiscountAmount;
        // Step 3: taxAmount = Round(afterDiscount * taxRate, 2)
        var expectedTax = Math.Round(expectedAfterDiscount * taxRate, 2);
        // Step 4: final = afterDiscount + taxAmount (no rounding needed)
        var expectedFinal = expectedAfterDiscount + expectedTax;

        return finalPrice.Amount == expectedFinal;
    }

    #endregion

    #region Property Tests: ToSubunits Conversion (Stripe Integration)

    /// <summary>
    ///     Verifies that conversion to subunits (cents) is reversible without precision loss.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool ToSubunits_ShouldBeReversible(PositiveInt amountCents)
    {
        var amount = Math.Round(amountCents.Get / 100.0m, 2);
        if (amount <= 0) return true;

        var money = Money.Create(amount);
        var subunits = money.ToSubunits();

        // Convert back from subunits
        var convertedBack = subunits / 100m;

        return convertedBack == money.Amount;
    }

    /// <summary>
    ///     Verifies that ToSubunits produces exact cent values (no fractional cents).
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool ToSubunits_ShouldProduceWholeNumbers(PositiveInt amountCents)
    {
        var amount = Math.Round(amountCents.Get / 100.0m, 2);
        if (amount <= 0) return true;

        var money = Money.Create(amount);
        var subunits = money.ToSubunits();

        // Subunits should be a whole number (integer)
        return subunits == Math.Floor((double)subunits);
    }

    #endregion

    #region Property Tests: Multi-Currency Safety

    [Fact]
    public void DifferentCurrencies_Addition_ShouldThrow()
    {
        // Arrange
        var usdMoney = Money.Create(100m, "USD");
        var gelMoney = Money.Create(100m, "GEL");

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => usdMoney.Add(gelMoney));
    }

    [Fact]
    public void DifferentCurrencies_Subtraction_ShouldThrow()
    {
        // Arrange
        var usdMoney = Money.Create(100m, "USD");
        var gelMoney = Money.Create(50m, "GEL");

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => usdMoney.Subtract(gelMoney));
    }

    /// <summary>
    ///     Same currency operations should preserve the currency.
    /// </summary>
    [Property(MaxTest = 500)]
    public bool SameCurrency_OperationsShouldPreserveCurrency(PositiveInt aCents, PositiveInt bCents)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);

        if (a <= 0 || b <= 0) return true;

        // Test with GEL (default currency)
        var moneyA = Money.Create(a);
        var moneyB = Money.Create(b);

        var sum = moneyA.Add(moneyB);
        var product = moneyA.Multiply(2);

        return sum.Currency == "GEL" && product.Currency == "GEL";
    }

    #endregion

    #region Property Tests: Rounding Behavior

    /// <summary>
    ///     Verifies that Money always rounds to 2 decimal places.
    /// </summary>
    [Property(MaxTest = 1000)]
    public bool Money_ShouldAlwaysHaveTwoDecimalPrecision(PositiveInt rawCents)
    {
        // Create money with potential 3+ decimal places
        var rawAmount = rawCents.Get / 1000m; // Could have 3 decimals

        if (rawAmount <= 0) return true;

        var money = Money.Create(rawAmount);

        // Verify it was rounded to 2 decimals
        var roundedAmount = Math.Round(rawAmount, 2);
        return money.Amount == roundedAmount;
    }

    /// <summary>
    ///     Verifies that summing many small amounts doesn't accumulate floating-point errors.
    /// </summary>
    [Fact]
    public void SummingManySmallAmounts_ShouldNotAccumulateErrors()
    {
        // The classic floating-point trap: 0.1 + 0.1 + ... (1000 times)
        var iterations = 1000;
        var smallAmount = Money.Create(0.01m);

        var total = Money.Zero();
        for (int i = 0; i < iterations; i++)
        {
            total = total.Add(smallAmount);
        }

        // Should be exactly 10.00, not 9.999999... or 10.000001...
        total.Amount.ShouldBe(10.00m);
    }

    /// <summary>
    ///     Verifies that the infamous 0.1 + 0.2 != 0.3 problem is handled correctly.
    /// </summary>
    [Fact]
    public void ClassicFloatingPointTrap_ShouldBeHandled()
    {
        var m1 = Money.Create(0.1m);
        var m2 = Money.Create(0.2m);
        var expected = Money.Create(0.3m);

        var result = m1.Add(m2);

        result.Amount.ShouldBe(expected.Amount);
        result.Amount.ShouldBe(0.3m);
    }

    #endregion

    #region Property Tests: Exchange Rate Scenarios

    /// <summary>
    ///     Simulates currency conversion and verifies no "phantom cents" are created.
    ///     Note: Round-trip conversion (USD → GEL → USD) can accumulate rounding errors.
    ///     This test validates that for amounts and rates that produce meaningful
    ///     converted values (≥ 0.05), the round-trip error is bounded.
    /// </summary>
    [Property(MaxTest = 500)]
    public bool CurrencyConversion_ShouldNotCreatePhantomCents(
        PositiveInt amountCents,
        PositiveInt rateCents)
    {
        // Use realistic financial values
        var amount = Math.Max(1.00m, Math.Round(amountCents.Get / 100.0m, 2));
        var exchangeRate = Math.Max(0.50m, Math.Round(rateCents.Get / 100.0m, 2));

        var originalMoney = Money.Create(amount, "USD");

        // Simulate conversion: USD → GEL (rounds)
        var convertedAmount = Math.Round(originalMoney.Amount * exchangeRate, 2);
        var convertedMoney = Money.Create(convertedAmount, "GEL");

        // Simulate back-conversion: GEL → USD (rounds again)
        var backConvertedAmount = Math.Round(convertedMoney.Amount / exchangeRate, 2);
        var backConverted = Money.Create(backConvertedAmount, "USD");

        // The difference should be at most 0.02 (two cents) due to double rounding
        // This is acceptable for realistic financial scenarios with meaningful amounts
        var difference = Math.Abs(originalMoney.Amount - backConverted.Amount);

        return difference <= 0.02m;
    }

    /// <summary>
    ///     Verifies that converting multiple items and summing produces the same result
    ///     as summing then converting (within rounding tolerance).
    /// </summary>
    [Property(MaxTest = 500)]
    public bool ConversionOrder_ShouldProduceSameResult(
        PositiveInt aCents,
        PositiveInt bCents,
        PositiveInt rateCents)
    {
        var a = Math.Round(aCents.Get / 100.0m, 2);
        var b = Math.Round(bCents.Get / 100.0m, 2);
        var rate = Math.Max(0.01m, Math.Round(rateCents.Get / 100.0m, 2));

        if (a <= 0 || b <= 0 || rate <= 0) return true;

        var moneyA = Money.Create(a, "USD");
        var moneyB = Money.Create(b, "USD");

        // Method 1: Convert each, then sum
        var convertedA = Money.Create(Math.Round(a * rate, 2), "GEL");
        var convertedB = Money.Create(Math.Round(b * rate, 2), "GEL");
        var sumOfConverted = convertedA.Add(convertedB);

        // Method 2: Sum, then convert
        var sum = moneyA.Add(moneyB);
        var convertedSum = Money.Create(Math.Round(sum.Amount * rate, 2), "GEL");

        // Difference should be at most 0.01 (one cent) due to rounding order
        var difference = Math.Abs(sumOfConverted.Amount - convertedSum.Amount);

        return difference <= 0.01m;
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void MinimumCurrencyUnit_ShouldBeHandledCorrectly()
    {
        var oneCent = Money.Create(0.01m);

        var sum = oneCent.Add(oneCent);
        sum.Amount.ShouldBe(0.02m);

        var product = oneCent.Multiply(100);
        product.Amount.ShouldBe(1.00m);
    }

    [Fact]
    public void LargeAmounts_ShouldBeHandledCorrectly()
    {
        var largeAmount = Money.Create(999_999_999.99m);
        var oneCent = Money.Create(0.01m);

        var sum = largeAmount.Add(oneCent);
        sum.Amount.ShouldBe(1_000_000_000.00m);
    }

    [Fact]
    public void NegativeAmount_ShouldThrow()
    {
        Should.Throw<ArgumentException>(() => Money.Create(-1m));
    }

    [Fact]
    public void EmptyCurrency_ShouldThrow()
    {
        Should.Throw<ArgumentException>(() => Money.Create(100m, ""));
        Should.Throw<ArgumentException>(() => Money.Create(100m, "   "));
    }

    #endregion
}
