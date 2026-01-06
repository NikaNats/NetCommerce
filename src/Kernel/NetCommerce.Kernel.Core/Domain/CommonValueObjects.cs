#nullable enable
namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
///     Represents an email address value object.
/// </summary>
public sealed class Email : ValueObject
{
    private Email(string value)
    {
        Value = value;
    }

    public string Value { get; }

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
///     Represents a phone number value object.
/// </summary>
public sealed class PhoneNumber : ValueObject
{
    private PhoneNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static PhoneNumber Create(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Phone number cannot be empty", nameof(phoneNumber));

        // Remove all non-digit characters for storage
        var normalized = new string(phoneNumber.Where(char.IsDigit).ToArray());

        if (normalized.Length < 7 || normalized.Length > 15)
            throw new ArgumentException("Invalid phone number length", nameof(phoneNumber));

        return new PhoneNumber(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(PhoneNumber phone) => phone.Value;
}

/// <summary>
///     Represents a percentage value object (0-100 or 0-1 range).
/// </summary>
public sealed class Percentage : ValueObject
{
    private Percentage(decimal value)
    {
        Value = value;
    }

    /// <summary>
    ///     The percentage value as a decimal (e.g., 0.18 for 18%).
    /// </summary>
    public decimal Value { get; }

    /// <summary>
    ///     Creates a percentage from a decimal value (e.g., 0.18 for 18%).
    /// </summary>
    public static Percentage FromDecimal(decimal value)
    {
        if (value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(nameof(value), "Percentage must be between 0 and 1");

        return new Percentage(Math.Round(value, 4));
    }

    /// <summary>
    ///     Creates a percentage from a whole number (e.g., 18 for 18%).
    /// </summary>
    public static Percentage FromWholeNumber(int value)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "Percentage must be between 0 and 100");

        return new Percentage(Math.Round(value / 100m, 4));
    }

    /// <summary>
    ///     Returns the percentage as a whole number (e.g., 18 for 0.18).
    /// </summary>
    public int ToWholeNumber() => (int)Math.Round(Value * 100);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value:P2}";
}

/// <summary>
///     Represents a non-empty string value object.
/// </summary>
public sealed class NonEmptyString : ValueObject
{
    private NonEmptyString(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static NonEmptyString Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or empty", nameof(value));

        return new NonEmptyString(value.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(NonEmptyString value) => value.Value;
}
