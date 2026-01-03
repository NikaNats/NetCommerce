using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Results;
using Wolverine.Attributes;

namespace NetCommerce.Catalog.Application.Products.Commands;

/// <summary>
///     Wolverine handler for CreateProductCommand.
///     Uses static method pattern with method injection.
/// </summary>
[WolverineHandler]
public static class CreateProductHandler
{
    public static async Task<Result<Guid>> HandleAsync(
        CreateProductCommand command,
        CatalogDbContext db,
        ILogger<CreateProductCommand> logger,
        CancellationToken cancellationToken)
    {
        // Check if SKU already exists
        var exists = await db.Products.AnyAsync(p => p.Sku == command.Sku, cancellationToken);
        if (exists)
            return Result.Failure<Guid>(
                Error.Conflict($"Product with SKU '{command.Sku}' already exists"));

        var price = Money.Create(command.Price, command.Currency);

        var product = Product.Create(
            command.Name,
            command.Description,
            command.Sku,
            price,
            command.CategoryId);

        db.Products.Add(product);

        logger.LogInformation(
            "Product {ProductId} created with SKU {Sku}",
            product.Id, command.Sku);

        return product.Id;
    }
}

/// <summary>
///     Wolverine handler for UpdateProductCommand.
/// </summary>
[WolverineHandler]
public static class UpdateProductHandler
{
    public static async Task<Result> HandleAsync(
        UpdateProductCommand command,
        CatalogDbContext db,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([command.ProductId], cancellationToken);

        if (product is null)
            return Result.Failure(Error.NotFound(nameof(Product), command.ProductId));

        product.UpdateDetails(command.Name, command.Description, command.Sku);

        return Result.Success();
    }
}

/// <summary>
///     Wolverine handler for UpdateProductPriceCommand.
/// </summary>
[WolverineHandler]
public static class UpdateProductPriceHandler
{
    public static async Task<Result> HandleAsync(
        UpdateProductPriceCommand command,
        CatalogDbContext db,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([command.ProductId], cancellationToken);

        if (product is null)
            return Result.Failure(Error.NotFound(nameof(Product), command.ProductId));

        var newPrice = Money.Create(command.NewPrice, command.Currency);
        product.UpdatePrice(newPrice);

        return Result.Success();
    }
}

/// <summary>
///     Wolverine handler for PublishProductCommand.
/// </summary>
[WolverineHandler]
public static class PublishProductHandler
{
    public static async Task<Result> HandleAsync(
        PublishProductCommand command,
        CatalogDbContext db,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([command.ProductId], cancellationToken);

        if (product is null)
            return Result.Failure(Error.NotFound(nameof(Product), command.ProductId));

        product.Publish();

        return Result.Success();
    }
}

/// <summary>
///     Wolverine handler for AddProductImageCommand.
/// </summary>
[WolverineHandler]
public static class AddProductImageHandler
{
    public static async Task<Result> HandleAsync(
        AddProductImageCommand command,
        CatalogDbContext db,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([command.ProductId], cancellationToken);

        if (product is null)
            return Result.Failure(Error.NotFound(nameof(Product), command.ProductId));

        product.AddImage(command.ImageKey, command.DisplayOrder, command.IsPrimary);

        return Result.Success();
    }
}
