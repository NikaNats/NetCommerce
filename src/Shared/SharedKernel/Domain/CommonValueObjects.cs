namespace NetCommerce.SharedKernel.Domain;

/// <summary>
/// Common value objects used across modules.
/// </summary>

/// <summary>
/// Represents a monetary value with currency.
/// </summary>
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency = "GEL")
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));
        
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));

        return new Money(Math.Round(amount, 2), currency.ToUpperInvariant());
    }

    public static Money Zero(string currency = "GEL") => new(0, currency);

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

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException($"Cannot perform operation on different currencies: {Currency} and {other.Currency}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}

/// <summary>
/// Represents an email address.
/// </summary>
public sealed class Email : ValueObject
{
    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty", nameof(email));
        
        if (!email.Contains('@') || !email.Contains('.'))
            throw new ArgumentException("Invalid email format", nameof(email));

        return new Email(email.Trim().ToLowerInvariant());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
    
    public static implicit operator string(Email email) => email.Value;
}

/// <summary>
/// Represents an address.
/// </summary>
public sealed class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string Country { get; }
    public string PostalCode { get; }

    private Address(string street, string city, string state, string country, string postalCode)
    {
        Street = street;
        City = city;
        State = state;
        Country = country;
        PostalCode = postalCode;
    }

    public static Address Create(string street, string city, string state, string country, string postalCode)
    {
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street is required", nameof(street));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required", nameof(city));
        if (string.IsNullOrWhiteSpace(country))
            throw new ArgumentException("Country is required", nameof(country));

        return new Address(street.Trim(), city.Trim(), state?.Trim() ?? "", country.Trim(), postalCode?.Trim() ?? "");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return Country;
        yield return PostalCode;
    }

    public override string ToString() => $"{Street}, {City}, {State} {PostalCode}, {Country}";
}
