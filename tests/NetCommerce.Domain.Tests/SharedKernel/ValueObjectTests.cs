using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Core.Domain;
using Shouldly;

namespace NetCommerce.Domain.Tests.SharedKernel;

/// <summary>
///     Unit tests for Money value object.
/// </summary>
public class MoneyTests
{
    #region ToString Tests

    [Fact]
    public void ToString_ShouldContainCurrencyCode()
    {
        // Arrange
        var money = Money.Create(1234.56m);

        // Act
        var result = money.ToString();

        // Assert - just check currency code exists (formatting is locale-dependent)
        result.ShouldContain("GEL");
        // Amount should be present in some form
        result.ShouldNotBeNullOrWhiteSpace();
    }

    #endregion

    #region Create Tests

    [Fact]
    public void Create_WithValidData_ShouldCreateMoney()
    {
        // Act
        var money = Money.Create(99.99m);

        // Assert
        money.Amount.ShouldBe(99.99m);
        money.Currency.ShouldBe("GEL");
    }

    [Fact]
    public void Create_ShouldRoundToTwoDecimals()
    {
        // Act
        var money = Money.Create(99.999m);

        // Assert
        money.Amount.ShouldBe(100.00m);
    }

    [Fact]
    public void Create_ShouldUppercaseCurrency()
    {
        // Act
        var money = Money.Create(10m, "gel");

        // Assert
        money.Currency.ShouldBe("GEL");
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => Money.Create(-10m))
            .Message.ShouldContain("negative");
    }

    [Fact]
    public void Create_WithEmptyCurrency_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => Money.Create(10m, ""))
            .Message.ShouldContain("Currency");
    }

    [Fact]
    public void Zero_ShouldReturnZeroAmount()
    {
        // Act
        var money = Money.Zero();

        // Assert
        money.Amount.ShouldBe(0m);
        money.Currency.ShouldBe("GEL");
    }

    #endregion

    #region Add Tests

    [Fact]
    public void Add_WithSameCurrency_ShouldAddAmounts()
    {
        // Arrange
        var money1 = Money.Create(50m);
        var money2 = Money.Create(30m);

        // Act
        var result = money1.Add(money2);

        // Assert
        result.Amount.ShouldBe(80m);
        result.Currency.ShouldBe("GEL");
    }

    [Fact]
    public void Add_WithDifferentCurrency_ShouldThrowException()
    {
        // Arrange
        var money1 = Money.Create(50m);
        var money2 = Money.Create(30m, "USD");

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => money1.Add(money2))
            .Message.ShouldContain("different currencies");
    }

    #endregion

    #region Subtract Tests

    [Fact]
    public void Subtract_WithSameCurrency_ShouldSubtractAmounts()
    {
        // Arrange
        var money1 = Money.Create(50m);
        var money2 = Money.Create(30m);

        // Act
        var result = money1.Subtract(money2);

        // Assert
        result.Amount.ShouldBe(20m);
    }

    [Fact]
    public void Subtract_WithDifferentCurrency_ShouldThrowException()
    {
        // Arrange
        var money1 = Money.Create(50m);
        var money2 = Money.Create(30m, "USD");

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => money1.Subtract(money2));
    }

    #endregion

    #region Multiply Tests

    [Fact]
    public void Multiply_ShouldMultiplyAmount()
    {
        // Arrange
        var money = Money.Create(10m);

        // Act
        var result = money.Multiply(3);

        // Assert
        result.Amount.ShouldBe(30m);
        result.Currency.ShouldBe("GEL");
    }

    [Fact]
    public void Multiply_ShouldRoundResult()
    {
        // Arrange
        var money = Money.Create(10m);

        // Act
        var result = money.Multiply(0.333m);

        // Assert
        result.Amount.ShouldBe(3.33m);
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void Equals_WithSameValues_ShouldReturnTrue()
    {
        // Arrange
        var money1 = Money.Create(50m);
        var money2 = Money.Create(50m);

        // Assert
        money1.Equals(money2).ShouldBeTrue();
        (money1 == money2).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WithDifferentAmounts_ShouldReturnFalse()
    {
        // Arrange
        var money1 = Money.Create(50m);
        var money2 = Money.Create(60m);

        // Assert
        money1.Equals(money2).ShouldBeFalse();
    }

    [Fact]
    public void Equals_WithDifferentCurrencies_ShouldReturnFalse()
    {
        // Arrange
        var money1 = Money.Create(50m);
        var money2 = Money.Create(50m, "USD");

        // Assert
        money1.Equals(money2).ShouldBeFalse();
    }

    #endregion
}

/// <summary>
///     Unit tests for Email value object.
/// </summary>
public class EmailTests
{
    [Fact]
    public void Create_WithValidEmail_ShouldCreateEmail()
    {
        // Act
        var email = Email.Create("test@example.com");

        // Assert
        email.Value.ShouldBe("test@example.com");
    }

    [Fact]
    public void Create_ShouldLowercaseAndTrim()
    {
        // Act
        var email = Email.Create("  TEST@EXAMPLE.COM  ");

        // Assert
        email.Value.ShouldBe("test@example.com");
    }

    [Fact]
    public void Create_WithEmptyEmail_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => Email.Create(""));
    }

    [Fact]
    public void Create_WithInvalidFormat_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => Email.Create("invalid-email"));
    }

    [Fact]
    public void ImplicitConversion_ShouldReturnValue()
    {
        // Arrange
        var email = Email.Create("test@example.com");

        // Act
        string value = email;

        // Assert
        value.ShouldBe("test@example.com");
    }
}
