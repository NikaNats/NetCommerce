using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
///     Why an order was cancelled. Single place for trimming + length rules
///     so handlers, admin recovery, and the saga all store the same shape.
///     Matches <c>orders.cancellation_reason varchar(500)</c> — storage stays string.
/// </summary>
public sealed class CancellationReason : ValueObject
{
    public const int MaxLength = 500;

    private CancellationReason(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static CancellationReason Create(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason is required.", nameof(reason));

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxLength)
            throw new ArgumentOutOfRangeException(nameof(reason), $"Reason exceeds maximum of {MaxLength} characters.");

        return new CancellationReason(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(CancellationReason reason) => reason.Value;
}
