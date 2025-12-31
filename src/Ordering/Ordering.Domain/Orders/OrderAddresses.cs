using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
/// Shipping address value object.
/// </summary>
public sealed class ShippingAddress : ValueObject
{
    public string RecipientName { get; }
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string Country { get; }
    public string PostalCode { get; }
    public string Phone { get; }

    private ShippingAddress(
        string recipientName,
        string street,
        string city,
        string state,
        string country,
        string postalCode,
        string phone)
    {
        RecipientName = recipientName;
        Street = street;
        City = city;
        State = state;
        Country = country;
        PostalCode = postalCode;
        Phone = phone;
    }

    public static ShippingAddress Create(
        string recipientName,
        string street,
        string city,
        string state,
        string country,
        string postalCode,
        string phone)
    {
        if (string.IsNullOrWhiteSpace(recipientName))
            throw new ArgumentException("Recipient name is required", nameof(recipientName));
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street is required", nameof(street));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required", nameof(city));
        if (string.IsNullOrWhiteSpace(country))
            throw new ArgumentException("Country is required", nameof(country));

        return new ShippingAddress(
            recipientName.Trim(),
            street.Trim(),
            city.Trim(),
            state?.Trim() ?? string.Empty,
            country.Trim(),
            postalCode?.Trim() ?? string.Empty,
            phone?.Trim() ?? string.Empty);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return RecipientName;
        yield return Street;
        yield return City;
        yield return State;
        yield return Country;
        yield return PostalCode;
        yield return Phone;
    }
}

/// <summary>
/// Billing address value object.
/// </summary>
public sealed class BillingAddress : ValueObject
{
    public string Name { get; }
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string Country { get; }
    public string PostalCode { get; }

    private BillingAddress(
        string name,
        string street,
        string city,
        string state,
        string country,
        string postalCode)
    {
        Name = name;
        Street = street;
        City = city;
        State = state;
        Country = country;
        PostalCode = postalCode;
    }

    public static BillingAddress Create(
        string name,
        string street,
        string city,
        string state,
        string country,
        string postalCode)
    {
        return new BillingAddress(
            name?.Trim() ?? string.Empty,
            street?.Trim() ?? string.Empty,
            city?.Trim() ?? string.Empty,
            state?.Trim() ?? string.Empty,
            country?.Trim() ?? string.Empty,
            postalCode?.Trim() ?? string.Empty);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return Street;
        yield return City;
        yield return State;
        yield return Country;
        yield return PostalCode;
    }
}
