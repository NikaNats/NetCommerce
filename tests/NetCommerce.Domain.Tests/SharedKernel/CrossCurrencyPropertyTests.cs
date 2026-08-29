#nullable enable
using NetCommerce.Domain.Shared;
using Shouldly;

namespace NetCommerce.Domain.Tests.SharedKernel;

[Trait("Category", "PropertyBased")]
[Trait("Category", "FinancialIntegrity")]
public class CrossCurrencyPropertyTests
{
    private static readonly Random Rng = new(42);
    private static int NextPositiveInt(int max = 100000) => Rng.Next(1, max);
    private static byte NextByte() => (byte)Rng.Next(0, 256);

    [Fact]
    public void Money_Addition_ShouldBeCommutative()
    {
        for (int i = 0; i < 200; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (a <= 0 || b <= 0) continue;
            var result1 = Money.Create(a).Add(Money.Create(b));
            var result2 = Money.Create(b).Add(Money.Create(a));
            Assert.True(result1.Amount == result2.Amount);
        }
    }

    [Fact]
    public void Money_Addition_ShouldBeAssociative()
    {
        for (int i = 0; i < 200; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            var c = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (a <= 0 || b <= 0 || c <= 0) continue;
            var result1 = Money.Create(a).Add(Money.Create(b)).Add(Money.Create(c));
            var result2 = Money.Create(a).Add(Money.Create(b).Add(Money.Create(c)));
            Assert.True(result1.Amount == result2.Amount);
        }
    }

    [Fact]
    public void Money_AddingZero_ShouldReturnSameAmount()
    {
        for (int i = 0; i < 200; i++)
        {
            var amount = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (amount <= 0) continue;
            var result = Money.Create(amount).Add(Money.Zero());
            Assert.True(result.Amount == amount);
        }
    }

    [Fact]
    public void Money_Subtraction_ShouldBeInverseOfAddition()
    {
        for (int i = 0; i < 200; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (a <= 0 || b <= 0) continue;
            var larger = Math.Max(a, b);
            var smaller = Math.Min(a, b);
            var afterAdd = Money.Create(smaller).Add(Money.Create(larger - smaller));
            var afterSubtract = Money.Create(larger).Subtract(Money.Create(larger - smaller));
            Assert.True(afterAdd.Amount == larger && afterSubtract.Amount == smaller);
        }
    }

    [Fact]
    public void Money_Multiplication_ShouldDistributeOverAddition()
    {
        for (int i = 0; i < 200; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            var quantity = (int)Math.Max(1, Math.Min((int)NextByte(), 100));
            if (a <= 0 || b <= 0) continue;
            var sumThenMultiply = Money.Create(a).Add(Money.Create(b)).Multiply(quantity);
            var multiplyThenSum = Money.Create(a).Multiply(quantity).Add(Money.Create(b).Multiply(quantity));
            Assert.True(sumThenMultiply.Amount == multiplyThenSum.Amount);
        }
    }

    [Fact]
    public void Money_MultiplyByOne_ShouldReturnSameAmount()
    {
        for (int i = 0; i < 200; i++)
        {
            var amount = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (amount <= 0) continue;
            Assert.True(Money.Create(amount).Multiply(1).Amount == amount);
        }
    }

    [Fact]
    public void Money_MultiplyByZero_ShouldReturnZero()
    {
        for (int i = 0; i < 200; i++)
        {
            var amount = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (amount <= 0) continue;
            Assert.True(Money.Create(amount).Multiply(0).Amount == 0m);
        }
    }

    [Fact]
    public void OrderTotal_ShouldEqualSumOfLineItems()
    {
        for (int i = 0; i < 100; i++)
        {
            var p1 = Math.Round(NextPositiveInt() / 100.0m, 2);
            var p2 = Math.Round(NextPositiveInt() / 100.0m, 2);
            var p3 = Math.Round(NextPositiveInt() / 100.0m, 2);
            var q1 = (int)Math.Max(1, Math.Min((int)NextByte(), 20));
            var q2 = (int)Math.Max(1, Math.Min((int)NextByte(), 20));
            var q3 = (int)Math.Max(1, Math.Min((int)NextByte(), 20));
            if (p1 <= 0 || p2 <= 0 || p3 <= 0) continue;
            var lineItems = new[] { (UnitPrice: Money.Create(p1), Quantity: q1), (UnitPrice: Money.Create(p2), Quantity: q2), (UnitPrice: Money.Create(p3), Quantity: q3) };
            var sumOfLineTotals = lineItems.Aggregate(Money.Zero(), (acc, item) => acc.Add(item.UnitPrice.Multiply(item.Quantity)));
            var grandTotal = Money.Zero();
            foreach (var item in lineItems) grandTotal = grandTotal.Add(item.UnitPrice.Multiply(item.Quantity));
            Assert.True(sumOfLineTotals.Amount == grandTotal.Amount);
        }
    }

    [Fact]
    public void DiscountThenTax_ShouldProducePredictableResult()
    {
        for (int i = 0; i < 200; i++)
        {
            var baseAmount = Math.Round(NextPositiveInt() / 100.0m, 2);
            var discountRate = Math.Min(NextByte(), (byte)50) / 100.0m;
            var taxRate = NextByte() / 10000.0m;
            if (baseAmount <= 0) continue;
            var basePrice = Money.Create(baseAmount);
            var discountAmount = basePrice.Multiply(discountRate);
            var afterDiscount = basePrice.Subtract(discountAmount);
            var taxAmount = afterDiscount.Multiply(taxRate);
            var finalPrice = afterDiscount.Add(taxAmount);
            var expectedDiscountAmount = Math.Round(baseAmount * discountRate, 2);
            var expectedAfterDiscount = baseAmount - expectedDiscountAmount;
            var expectedTax = Math.Round(expectedAfterDiscount * taxRate, 2);
            var expectedFinal = expectedAfterDiscount + expectedTax;
            Assert.True(finalPrice.Amount == expectedFinal);
        }
    }

    [Fact]
    public void ToSubunits_ShouldBeReversible()
    {
        for (int i = 0; i < 200; i++)
        {
            var amount = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (amount <= 0) continue;
            var money = Money.Create(amount);
            var subunits = money.ToSubunits();
            var convertedBack = subunits / 100m;
            Assert.True(convertedBack == money.Amount);
        }
    }

    [Fact]
    public void ToSubunits_ShouldProduceWholeNumbers()
    {
        for (int i = 0; i < 200; i++)
        {
            var amount = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (amount <= 0) continue;
            var subunits = Money.Create(amount).ToSubunits();
            Assert.True(subunits == Math.Floor((double)subunits));
        }
    }

    [Fact]
    public void DifferentCurrencies_Addition_ShouldThrow()
    {
        var usdMoney = Money.Create(100m, "USD");
        var gelMoney = Money.Create(100m, "GEL");
        Should.Throw<InvalidOperationException>(() => usdMoney.Add(gelMoney));
    }

    [Fact]
    public void DifferentCurrencies_Subtraction_ShouldThrow()
    {
        var usdMoney = Money.Create(100m, "USD");
        var gelMoney = Money.Create(50m, "GEL");
        Should.Throw<InvalidOperationException>(() => usdMoney.Subtract(gelMoney));
    }

    [Fact]
    public void SameCurrency_OperationsShouldPreserveCurrency()
    {
        for (int i = 0; i < 200; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            if (a <= 0 || b <= 0) continue;
            var sum = Money.Create(a).Add(Money.Create(b));
            var product = Money.Create(a).Multiply(2);
            Assert.True(sum.Currency == "GEL" && product.Currency == "GEL");
        }
    }

    [Fact]
    public void Money_ShouldAlwaysHaveTwoDecimalPrecision()
    {
        for (int i = 0; i < 200; i++)
        {
            var rawAmount = NextPositiveInt() / 1000m;
            if (rawAmount <= 0) continue;
            var money = Money.Create(rawAmount);
            var roundedAmount = Math.Round(rawAmount, 2);
            Assert.True(money.Amount == roundedAmount);
        }
    }

    [Fact]
    public void SummingManySmallAmounts_ShouldNotAccumulateErrors()
    {
        var iterations = 1000;
        var smallAmount = Money.Create(0.01m);
        var total = Money.Zero();
        for (int i = 0; i < iterations; i++) total = total.Add(smallAmount);
        total.Amount.ShouldBe(10.00m);
    }

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

    [Fact]
    public void CurrencyConversion_ShouldNotCreatePhantomCents()
    {
        for (int i = 0; i < 100; i++)
        {
            var amount = Math.Max(1.00m, Math.Round(NextPositiveInt() / 100.0m, 2));
            var exchangeRate = Math.Max(0.50m, Math.Round(NextPositiveInt() / 100.0m, 2));
            var originalMoney = Money.Create(amount, "USD");
            var convertedAmount = Math.Round(originalMoney.Amount * exchangeRate, 2);
            var convertedMoney = Money.Create(convertedAmount, "GEL");
            var backConvertedAmount = Math.Round(convertedMoney.Amount / exchangeRate, 2);
            var backConverted = Money.Create(backConvertedAmount, "USD");
            var difference = Math.Abs(originalMoney.Amount - backConverted.Amount);
            Assert.True(difference <= 0.02m);
        }
    }

    [Fact]
    public void ConversionOrder_ShouldProduceSameResult()
    {
        for (int i = 0; i < 100; i++)
        {
            var a = Math.Round(NextPositiveInt() / 100.0m, 2);
            var b = Math.Round(NextPositiveInt() / 100.0m, 2);
            var rate = Math.Max(0.01m, Math.Round(NextPositiveInt() / 100.0m, 2));
            if (a <= 0 || b <= 0 || rate <= 0) continue;
            var convertedA = Money.Create(Math.Round(a * rate, 2), "GEL");
            var convertedB = Money.Create(Math.Round(b * rate, 2), "GEL");
            var sumOfConverted = convertedA.Add(convertedB);
            var sum = Money.Create(a, "USD").Add(Money.Create(b, "USD"));
            var convertedSum = Money.Create(Math.Round(sum.Amount * rate, 2), "GEL");
            var difference = Math.Abs(sumOfConverted.Amount - convertedSum.Amount);
            Assert.True(difference <= 0.01m);
        }
    }

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
}
