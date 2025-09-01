namespace Catalog.Domain.Enums;

/// <summary>
/// Represents the data types that can be used for product attributes.
/// </summary>
public enum DataType
{
    /// <summary>
    /// Text/string value.
    /// </summary>
    Text = 0,
    
    /// <summary>
    /// Numeric/integer value.
    /// </summary>
    Number = 1,
    
    /// <summary>
    /// Boolean true/false value.
    /// </summary>
    Boolean = 2,
    
    /// <summary>
    /// Date/time value.
    /// </summary>
    DateTime = 3,
    
    /// <summary>
    /// Decimal/floating point value.
    /// </summary>
    Decimal = 4
}