using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
///     Weight in kilograms captured at order time (snapshot).
///     Validation-only value object: storage stays <c>decimal</c> in
///     <see cref="OrderItem.AppliedWeightKg"/> to avoid an EF migration;
///     use <see cref="Create"/> at domain boundaries to reject meaningless weights.
/// </summary>
public sealed class WeightKg : ValueObject
{
    /// <summary>Upper bound that rejects data-entry errors without blocking real freight.</summary>
    public const decimal MaxKilograms = 10_000m;

    private WeightKg(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public static WeightKg Create(decimal value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Weight cannot be negative.");
        if (value > MaxKilograms)
            throw new ArgumentOutOfRangeException(nameof(value), $"Weight exceeds maximum of {MaxKilograms} kg.");

        return new WeightKg(Math.Round(value, 3, MidpointRounding.AwayFromZero));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value} kg";

    public static implicit operator decimal(WeightKg weight) => weight.Value;
}
