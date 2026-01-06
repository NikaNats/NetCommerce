#nullable enable
using System.Text.Json.Serialization;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Domain.Shared;

/// <summary>
///     NetCommerce-specific monetary value with GEL as default currency.
///     Note: This is project-specific due to the hardcoded default currency.
///     For universal use, either inject default currency via configuration or require explicit currency.
/// </summary>
public sealed class Money : ValueObject
{
    /// <summary>
    ///     JSON constructor for deserialization (e.g., saga state persistence).
    /// </summary>
    [JsonConstructor]
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    /// <summary>
    ///     Creates money with the NetCommerce default currency (GEL).
    ///     For universal applications, use the overload that requires explicit currency.
    /// </summary>
    public static Money Create(decimal amount, string currency = "GEL")
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));

        return new Money(Math.Round(amount, 2), currency.ToUpperInvariant());
    }

    /// <summary>
    ///     Creates zero money with the NetCommerce default currency.
    /// </summary>
    public static Money Zero(string currency = "GEL")
    {
        return new Money(0, currency);
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal multiplier)
    {
        return new Money(Math.Round(Amount * multiplier, 2), Currency);
    }

    /// <summary>
    ///     Converts the amount to minor currency units (e.g., cents) using banker-safe rounding.
    /// </summary>
    public long ToSubunits() => Convert.ToInt64(Math.Round(Amount * 100, 0, MidpointRounding.AwayFromZero));

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException(
                $"Cannot perform operation on different currencies: {Currency} and {other.Currency}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString()
    {
        return $"{Amount:N2} {Currency}";
    }
}
