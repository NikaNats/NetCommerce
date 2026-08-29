#nullable enable
using NetCommerce.Domain.Shared;
using Shouldly;

namespace NetCommerce.Domain.Tests.Finance;

/// <summary>
/// Migrated from FsCheck Property to xUnit v3 Fact with random loops (FsCheck not compatible with xunit.v3 4.0).
/// </summary>
[Trait("Category", "Financial")]
[Trait("Category", "PropertyBased")]
public class TriplePassPricingPropertyTests
{
    private static readonly Random Rng = new(42);

    private static int NextPositiveInt(int max = 100000) => Rng.Next(1, max);
    private static byte NextByte() => (byte)Rng.Next(0, 256);

    [Fact]
    public void PennyPerfect_LineTotal_ShouldEqual_SubTotalPlusTax()
    {
        for (int i = 0; i < 200; i++)
        {
            var basePriceCents = NextPositiveInt();
            byte discountPct = NextByte();
            byte taxRateBps = NextByte();
            var quantity = NextPositiveInt(100);
            var basePrice = Math.Round(basePriceCents / 100.0m, 2);
            var discountPercent = Math.Min(discountPct, (byte)99) / 100.0m;
            var taxRate = taxRateBps / 10000.0m;
            var qty = Math.Max(1, Math.Min(quantity, 100));
            if (basePrice <= 0) continue;
            var discountAmount = Math.Round(basePrice * discountPercent, 2);
            var subTotalUnit = Math.Round(basePrice - discountAmount, 2);
            var taxAmount = Math.Round(subTotalUnit * taxRate, 2);
            try
            {
                var breakdown = new PriceBreakdown(basePrice, discountAmount, taxAmount, taxRate, "VAT", "GEL", qty);
                var expectedLineTotal = breakdown.LineSubTotal + breakdown.LineTaxTotal;
                Assert.True(breakdown.LineTotal == expectedLineTotal);
            }
            catch (ArgumentException) { }
        }
    }

    [Fact]
    public void PennyPerfect_FinalPrice_ShouldEqual_SubTotalPlusTax()
    {
        for (int i = 0; i < 200; i++)
        {
            var basePriceCents = NextPositiveInt();
            byte discountPct = NextByte();
            byte taxRateBps = NextByte();
            var basePrice = Math.Round(basePriceCents / 100.0m, 2);
            var discountPercent = Math.Min(discountPct, (byte)99) / 100.0m;
            var taxRate = taxRateBps / 10000.0m;
            if (basePrice <= 0) continue;
            var discountAmount = Math.Round(basePrice * discountPercent, 2);
            var subTotal = Math.Round(basePrice - discountAmount, 2);
            var taxAmount = Math.Round(subTotal * taxRate, 2);
            try
            {
                var breakdown = PriceBreakdown.Create(basePrice, discountAmount, taxAmount, taxRate, "VAT");
                var expected = breakdown.SubTotal + breakdown.TaxAmount;
                Assert.True(breakdown.FinalPrice == expected);
            }
            catch (ArgumentException) { }
        }
    }

    [Fact]
    public void Money_Add_ShouldBeCommutative()
    {
        for (int i = 0; i < 200; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (a <= 0 || b <= 0) continue;
            var moneyA = Money.Create(a);
            var moneyB = Money.Create(b);
            var sumAB = moneyA.Add(moneyB);
            var sumBA = moneyB.Add(moneyA);
            Assert.True(sumAB.Amount == sumBA.Amount && sumAB.Currency == sumBA.Currency);
        }
    }

    [Fact]
    public void Money_Add_ShouldBeAssociative()
    {
        for (int i = 0; i < 200; i++)
        {
            var a = Math.Round((NextPositiveInt() % 10000) / 100.0m, 2);
            var b = Math.Round((NextPositiveInt() % 10000) / 100.0m, 2);
            var c = Math.Round((NextPositiveInt() % 10000) / 100.0m, 2);
            if (a <= 0 || b <= 0 || c <= 0) continue;
            var moneyA = Money.Create(a);
            var moneyB = Money.Create(b);
            var moneyC = Money.Create(c);
            var left = moneyA.Add(moneyB).Add(moneyC);
            var right = moneyA.Add(moneyB.Add(moneyC));
            Assert.True(left.Amount == right.Amount);
        }
    }

    [Fact]
    public void Money_Add_ShouldPreserveNonNegativity()
    {
        for (int i = 0; i < 200; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (a <= 0 || b <= 0) continue;
            var sum = Money.Create(a).Add(Money.Create(b));
            Assert.True(sum.Amount >= 0);
        }
    }

    [Fact]
    public void Money_Multiply_ShouldPreserveNonNegativity()
    {
        for (int i = 0; i < 200; i++)
        {
            var amount = Math.Round(NextPositiveInt() / 100.0m, 2);
            var multiplier = NextPositiveInt() / 100.0m;
            if (amount <= 0) continue;
            var result = Money.Create(amount).Multiply(multiplier);
            Assert.True(result.Amount >= 0);
        }
    }

    [Fact]
    public void Money_ShouldAlwaysHaveAtMostTwoDecimalPlaces()
    {
        for (int i = 0; i < 200; i++)
        {
            var rawAmount = NextPositiveInt() / 1000.0m;
            try
            {
                var money = Money.Create(rawAmount);
                var scaled = money.Amount * 100;
                Assert.True(scaled == Math.Floor(scaled));
            }
            catch (ArgumentException) { }
        }
    }

    [Fact]
    public void Money_ToSubunits_ShouldBeConsistent()
    {
        for (int i = 0; i < 200; i++)
        {
            var amount = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (amount <= 0) continue;
            var money = Money.Create(amount);
            var subunits = money.ToSubunits();
            var expected = (long)Math.Round(money.Amount * 100, 0, MidpointRounding.AwayFromZero);
            Assert.True(subunits == expected);
        }
    }

    [Fact]
    public void Money_DifferentCurrencies_ShouldThrow()
    {
        for (int i = 0; i < 50; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (a <= 0 || b <= 0) continue;
            var moneyGEL = Money.Create(a, "GEL");
            var moneyUSD = Money.Create(b, "USD");
            bool threwAdd = false, threwSub = false;
            try { moneyGEL.Add(moneyUSD); } catch (InvalidOperationException) { threwAdd = true; }
            try { moneyGEL.Subtract(moneyUSD); } catch (InvalidOperationException) { threwSub = true; }
            Assert.True(threwAdd && threwSub);
        }
    }

    [Fact]
    public void TriplePass_OrderTotal_ShouldEqualSumOfLineTotals()
    {
        for (int iter = 0; iter < 100; iter++)
        {
            var inputs = new[] { (NextPositiveInt(), NextByte(), NextByte(), NextPositiveInt()), (NextPositiveInt(), NextByte(), NextByte(), NextPositiveInt()), (NextPositiveInt(), NextByte(), NextByte(), NextPositiveInt()) };
            var breakdowns = new List<PriceBreakdown>();
            foreach (var (bpCents, discPct, taxBps, qty) in inputs)
            {
                var basePrice = Math.Round(bpCents / 100.0m, 2);
                var discountPercent = Math.Min(discPct, (byte)99) / 100.0m;
                var taxRate = taxBps / 10000.0m;
                var quantity = Math.Max(1, Math.Min(qty, 100));
                if (basePrice <= 0) continue;
                var discountAmount = Math.Round(basePrice * discountPercent, 2);
                var subTotalUnit = Math.Round(basePrice - discountAmount, 2);
                var taxAmount = Math.Round(subTotalUnit * taxRate, 2);
                try { breakdowns.Add(new PriceBreakdown(basePrice, discountAmount, taxAmount, taxRate, "VAT", "GEL", quantity)); } catch (ArgumentException) { }
            }
            if (breakdowns.Count == 0) continue;
            var sumOfLineTotals = breakdowns.Sum(b => b.LineTotal);
            var sumOfParts = breakdowns.Sum(b => b.LineSubTotal) + breakdowns.Sum(b => b.LineTaxTotal);
            Assert.True(sumOfLineTotals == sumOfParts);
        }
    }

    [Fact]
    public void PriceBreakdown_ToMoney_ShouldPreserveValue()
    {
        for (int i = 0; i < 200; i++)
        {
            var basePriceCents = NextPositiveInt();
            byte discountPct = NextByte();
            byte taxRateBps = NextByte();
            var basePrice = Math.Round(basePriceCents / 100.0m, 2);
            var discountPercent = Math.Min(discountPct, (byte)99) / 100.0m;
            var taxRate = taxRateBps / 10000.0m;
            if (basePrice <= 0) continue;
            var discountAmount = Math.Round(basePrice * discountPercent, 2);
            var subTotal = Math.Round(basePrice - discountAmount, 2);
            var taxAmount = Math.Round(subTotal * taxRate, 2);
            try
            {
                var breakdown = PriceBreakdown.Create(basePrice, discountAmount, taxAmount, taxRate, "VAT");
                var money = breakdown.ToMoney();
                Assert.True(money.Amount == breakdown.FinalPrice && money.Currency == breakdown.Currency);
            }
            catch (ArgumentException) { }
        }
    }

    [Fact]
    public void RoundingBoundary_ShouldHandleCorrectly()
    {
        for (int i = 0; i < 200; i++)
        {
            var smallAmount = (NextPositiveInt() % 1000) / 1000.0m;
            if (smallAmount <= 0) continue;
            var money = Money.Create(smallAmount);
            var rounded = Math.Round(smallAmount, 2);
            Assert.True(money.Amount == rounded);
        }
    }

    [Fact]
    public void LargeAmounts_ShouldNotOverflow()
    {
        for (int i = 0; i < 50; i++)
        {
            var largeAmount = Math.Round((decimal)NextPositiveInt() * 1000, 2);
            if (largeAmount <= 0 || largeAmount > 1_000_000_000) continue;
            try
            {
                var money = Money.Create(largeAmount);
                var doubled = money.Add(money);
                Assert.True(doubled.Amount == largeAmount * 2);
            }
            catch (OverflowException) { Assert.True(false, "Should not overflow within limits"); }
        }
    }

    [Fact]
    public void PropertyTestSummary_OutputsConfiguration()
    {
        Console.WriteLine("Triple-Pass Pricing Property-Based Test Summary (migrated to xunit.v3 Fact loops)");
    }
}
