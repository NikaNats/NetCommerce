using Catalog.Domain.Common;

namespace Catalog.Domain.ValueObjects;

/// <summary>
/// Represents a price with amount and currency.
/// This value object ensures price validation and immutability.
/// </summary>
public class Price : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Price(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentException("Price amount cannot be negative", nameof(amount));
            
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be null or empty", nameof(currency));
            
        if (currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter code (e.g., USD, EUR)", nameof(currency));

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    /// <summary>
    /// Creates a default zero price in USD.
    /// </summary>
    public static Price Zero => new(0, "USD");

    /// <summary>
    /// Creates a zero price with the specified currency.
    /// </summary>
    /// <param name="currency">The currency code</param>
    /// <returns>A price with zero amount</returns>
    public static Price WithCurrency(string currency) => new(0, currency);

    /// <summary>
    /// Creates a price from amount and currency.
    /// </summary>
    /// <param name="amount">The price amount</param>
    /// <param name="currency">The currency code</param>
    /// <returns>A new Price instance</returns>
    public static Price From(decimal amount, string currency) => new(amount, currency);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:F2} {Currency}";
}