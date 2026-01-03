using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Application.Categories.Commands;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Results;
using Wolverine.Attributes;

namespace NetCommerce.Catalog.Infrastructure.Handlers;

/// <summary>
///     Wolverine handler for CreateCategoryCommand.
/// </summary>
[WolverineHandler]
public static class CreateCategoryHandler
{
    public static async Task<Result<Guid>> HandleAsync(
        CreateCategoryCommand command,
        CatalogDbContext db,
        ILogger<CreateCategoryCommand> logger,
        CancellationToken cancellationToken)
    {
        var slug = SlugGenerator.Generate(command.Name);
        
        // Check if category with same slug already exists
        var existingBySlug = await db.Categories
            .AnyAsync(c => c.Slug == slug, cancellationToken);

        if (existingBySlug)
            return Result.Failure<Guid>(
                Error.Conflict($"Category with name '{command.Name}' already exists."));

        // Validate parent exists if specified
        if (command.ParentCategoryId.HasValue)
        {
            var parentExists = await db.Categories
                .AnyAsync(c => c.Id == command.ParentCategoryId.Value, cancellationToken);

            if (!parentExists)
                return Result.Failure<Guid>(
                    Error.NotFound("ParentCategory", command.ParentCategoryId.Value));
        }

        var category = Category.Create(
            command.Name,
            command.Description,
            command.ParentCategoryId,
            command.DisplayOrder);

        db.Categories.Add(category);

        logger.LogInformation(
            "Category {CategoryId} created with name {CategoryName}",
            category.Id, command.Name);

        return category.Id;
    }
}

/// <summary>
///     Wolverine handler for UpdateCategoryCommand.
/// </summary>
[WolverineHandler]
public static class UpdateCategoryHandler
{
    public static async Task<Result> HandleAsync(
        UpdateCategoryCommand command,
        CatalogDbContext db,
        CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([command.CategoryId], cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("Category", command.CategoryId));

        category.Update(command.Name, command.Description, command.DisplayOrder);

        return Result.Success();
    }
}

/// <summary>
///     Wolverine handler for DeleteCategoryCommand.
/// </summary>
[WolverineHandler]
public static class DeleteCategoryHandler
{
    public static async Task<Result> HandleAsync(
        DeleteCategoryCommand command,
        CatalogDbContext db,
        CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([command.CategoryId], cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("Category", command.CategoryId));

        // Check for child categories
        var hasChildren = await db.Categories
            .AnyAsync(c => c.ParentCategoryId == command.CategoryId, cancellationToken);
        
        if (hasChildren)
            return Result.Failure(
                Error.Conflict("Cannot delete category with child categories."));

        db.Categories.Remove(category);

        return Result.Success();
    }
}

/// <summary>
///     Wolverine handler for SetCategoryActiveCommand.
/// </summary>
[WolverineHandler]
public static class SetCategoryActiveHandler
{
    public static async Task<Result> HandleAsync(
        SetCategoryActiveCommand command,
        CatalogDbContext db,
        CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([command.CategoryId], cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("Category", command.CategoryId));

        if (command.IsActive)
            category.Activate();
        else
            category.Deactivate();

        return Result.Success();
    }
}