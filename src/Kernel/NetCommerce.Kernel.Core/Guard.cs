#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NetCommerce.Kernel.Core;

/// <summary>
///     Standard validation helper for defensive programming.
///     Provides guard clauses that throw descriptive exceptions.
/// </summary>
public static class Guard
{
    /// <summary>
    ///     Throws ArgumentNullException if the value is null.
    /// </summary>
    public static T AgainstNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentException if the string is null, empty, or whitespace.
    /// </summary>
    public static string AgainstNullOrEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", parameterName);
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentException if the collection is null or empty.
    /// </summary>
    public static IReadOnlyCollection<T> AgainstNullOrEmpty<T>(
        [NotNull] IReadOnlyCollection<T>? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value is null || value.Count == 0)
        {
            throw new ArgumentException("Collection cannot be null or empty.", parameterName);
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentOutOfRangeException if the value is negative.
    /// </summary>
    public static decimal AgainstNegative(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentOutOfRangeException if the value is negative.
    /// </summary>
    public static int AgainstNegative(
        int value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentOutOfRangeException if the value is negative or zero.
    /// </summary>
    public static decimal AgainstNegativeOrZero(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentOutOfRangeException if the value is negative or zero.
    /// </summary>
    public static int AgainstNegativeOrZero(
        int value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentOutOfRangeException if the value is outside the specified range.
    /// </summary>
    public static T AgainstOutOfRange<T>(
        T value,
        T minimum,
        T maximum,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null) where T : IComparable<T>
    {
        if (value.CompareTo(minimum) < 0 || value.CompareTo(maximum) > 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"Value must be between {minimum} and {maximum}.");
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentException if the GUID is empty.
    /// </summary>
    public static Guid AgainstDefault(
        Guid value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("GUID cannot be empty.", parameterName);
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentException if the string exceeds the maximum length.
    /// </summary>
    public static string AgainstOverflow(
        string value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"String cannot exceed {maxLength} characters.", parameterName);
        }
        return value;
    }

    /// <summary>
    ///     Throws ArgumentException if the condition is not met.
    /// </summary>
    public static void Against(
        bool condition,
        string message,
        [CallerArgumentExpression(nameof(condition))] string? parameterName = null)
    {
        if (condition)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    /// <summary>
    ///     Throws InvalidOperationException if the condition is not met.
    /// </summary>
    public static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
