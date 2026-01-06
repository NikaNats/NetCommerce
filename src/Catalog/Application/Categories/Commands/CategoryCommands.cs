#nullable enable
using NetCommerce.Kernel.Application;

namespace NetCommerce.Catalog.Application.Categories.Commands;

/// <summary>
///     Command to create a new category.
/// </summary>
public record CreateCategoryCommand(
    string Name,
    string Description,
    Guid? ParentCategoryId,
    int DisplayOrder) : ICommand<Guid>;

/// <summary>
///     Command to update an existing category.
/// </summary>
public record UpdateCategoryCommand(
    Guid CategoryId,
    string Name,
    string Description,
    int DisplayOrder) : ICommand;

/// <summary>
///     Command to delete a category.
/// </summary>
public record DeleteCategoryCommand(Guid CategoryId) : ICommand;

/// <summary>
///     Command to set parent category.
/// </summary>
public record SetCategoryParentCommand(
    Guid CategoryId,
    Guid? ParentCategoryId) : ICommand;

/// <summary>
///     Command to activate or deactivate a category.
/// </summary>
public record SetCategoryActiveCommand(
    Guid CategoryId,
    bool IsActive) : ICommand;

/// <summary>
///     Command to set category image.
/// </summary>
public record SetCategoryImageCommand(
    Guid CategoryId,
    string? ImageKey) : ICommand;
