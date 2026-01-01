namespace NetCommerce.SharedKernel.Infrastructure;

/// <summary>
///     Clock abstraction for testable time operations.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
}

/// <summary>
///     Default implementation using system clock.
/// </summary>
public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}