using Catalog.Domain.Aggregates;
using Catalog.Domain.Repositories;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Services;

/// <summary>
/// Domain service that handles complex business logic related to product publishing.
/// This service encapsulates business rules that span multiple aggregates or require repository access.
/// </summary>
public class ProductPublishingService
{
    private readonly IVariantRepository _variantRepository;

    public ProductPublishingService(IVariantRepository variantRepository)
    {
        _variantRepository = variantRepository ?? throw new ArgumentNullException(nameof(variantRepository));
    }

    /// <summary>
    /// Checks if a product can be published by verifying business rules.
    /// This is a domain service because it requires checking variants (different aggregate).
    /// </summary>
    /// <param name="product">The product to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the product can be published</returns>
    public async Task<bool> CanPublishProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        if (product == null)
            throw new ArgumentNullException(nameof(product));

        // Business rule: Product must have at least one active variant to be published
        var activeVariantCount = await _variantRepository.CountActiveVariantsForProductAsync(product.Id, cancellationToken);
        return activeVariantCount > 0;
    }

    /// <summary>
    /// Publishes a product after verifying all business rules.
    /// This method orchestrates the publishing process across aggregates.
    /// </summary>
    /// <param name="product">The product to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the product was successfully published, false if business rules prevented it</returns>
    public async Task<bool> PublishProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        if (product == null)
            throw new ArgumentNullException(nameof(product));

        var canPublish = await CanPublishProductAsync(product, cancellationToken);
        
        if (canPublish)
        {
            product.Publish(hasActiveVariants: true);
            return true;
        }

        return false;
    }
}