#nullable enable
namespace NetCommerce.Kernel.Core.Infrastructure;

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

/// <summary>
///     Test implementation that allows setting the current time.
/// </summary>
public sealed class TestDateTimeProvider : IDateTimeProvider
{
    public TestDateTimeProvider(DateTime? utcNow = null)
    {
        UtcNow = utcNow ?? DateTime.UtcNow;
    }

    public DateTime UtcNow { get; set; }
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);

    public void Advance(TimeSpan timeSpan)
    {
        UtcNow = UtcNow.Add(timeSpan);
    }
}
