namespace NetCommerce.Catalog.Application.Products.Queries;

/// <summary>
/// CDN URL generator interface for product images.
/// </summary>
public interface ICdnUrlGenerator
{
    /// <summary>
    /// Generates a full CDN URL from an image key.
    /// </summary>
    string GenerateUrl(string imageKey);
}
