namespace Catalog.Domain.Enums;

/// <summary>
/// Represents the status of a product in its lifecycle.
/// </summary>
public enum ProductStatus
{
    /// <summary>
    /// Product is in draft state and not published.
    /// </summary>
    Draft = 0,
    
    /// <summary>
    /// Product is published and available.
    /// </summary>
    Published = 1,
    
    /// <summary>
    /// Product is archived and no longer available.
    /// </summary>
    Archived = 2
}