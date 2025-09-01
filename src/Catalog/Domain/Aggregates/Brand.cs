using Catalog.Domain.Common;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Aggregates;

/// <summary>
/// Brand aggregate root representing a product brand or manufacturer.
/// Provides brand information for product organization and marketing.
/// </summary>
public class Brand : AggregateRoot<BrandId>
{
    // State - encapsulated with private setters
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public bool IsActive { get; private set; }

    // Private constructor for entity framework and deserialization
    private Brand(BrandId id, string name) : base(id)
    {
        Name = name;
        Description = string.Empty;
        IsActive = true;
    }

    /// <summary>
    /// Factory method to create a new brand.
    /// </summary>
    /// <param name="id">The brand identifier</param>
    /// <param name="name">The brand name</param>
    /// <param name="description">Optional description</param>
    /// <returns>A new Brand instance</returns>
    public static Brand Create(BrandId id, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Brand name is required.");

        var brand = new Brand(id, name.Trim())
        {
            Description = description?.Trim() ?? string.Empty
        };

        return brand;
    }

    /// <summary>
    /// Updates the brand name.
    /// </summary>
    /// <param name="name">The new brand name</param>
    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Brand name is required.");

        Name = name.Trim();
    }

    /// <summary>
    /// Updates the brand description.
    /// </summary>
    /// <param name="description">The new description</param>
    public void UpdateDescription(string? description)
    {
        Description = description?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Sets the brand logo URL.
    /// </summary>
    /// <param name="logoUrl">The URL to the brand logo</param>
    public void SetLogoUrl(string? logoUrl)
    {
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
    }

    /// <summary>
    /// Sets the brand website URL.
    /// </summary>
    /// <param name="websiteUrl">The brand's website URL</param>
    public void SetWebsiteUrl(string? websiteUrl)
    {
        if (!string.IsNullOrWhiteSpace(websiteUrl))
        {
            var trimmedUrl = websiteUrl.Trim();
            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out _))
                throw new BusinessRuleException("Website URL must be a valid URL.");
                
            WebsiteUrl = trimmedUrl;
        }
        else
        {
            WebsiteUrl = null;
        }
    }

    /// <summary>
    /// Activates the brand, making it available for product association.
    /// </summary>
    public void Activate()
    {
        IsActive = true;
    }

    /// <summary>
    /// Deactivates the brand, preventing new product associations.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }
}