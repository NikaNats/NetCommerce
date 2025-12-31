using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Catalog.Application.Categories.Commands;

public sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, Guid>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        // Check if category with same name already exists
        var existingBySlug = await _categoryRepository.GetBySlugAsync(
            SlugGenerator.Generate(request.Name), cancellationToken);
        
        if (existingBySlug is not null)
        {
            return Result.Failure<Guid>(
                Error.Conflict($"Category with name '{request.Name}' already exists."));
        }

        // Validate parent exists if specified
        if (request.ParentCategoryId.HasValue)
        {
            var parent = await _categoryRepository.GetByIdAsync(
                request.ParentCategoryId.Value, cancellationToken);
            
            if (parent is null)
            {
                return Result.Failure<Guid>(
                    Error.NotFound("ParentCategory", request.ParentCategoryId.Value));
            }
        }

        var category = Category.Create(
            request.Name,
            request.Description,
            request.ParentCategoryId,
            request.DisplayOrder);

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return category.Id;
    }
}

public sealed class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        
        if (category is null)
        {
            return Result.Failure(Error.NotFound("Category", request.CategoryId));
        }

        category.Update(request.Name, request.Description, request.DisplayOrder);
        
        _categoryRepository.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class DeleteCategoryCommandHandler : ICommandHandler<DeleteCategoryCommand>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        
        if (category is null)
        {
            return Result.Failure(Error.NotFound("Category", request.CategoryId));
        }

        // Check for child categories
        var children = await _categoryRepository.GetChildCategoriesAsync(request.CategoryId, cancellationToken);
        if (children.Any())
        {
            return Result.Failure(
                Error.Conflict("Cannot delete category with child categories."));
        }

        _categoryRepository.Remove(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class SetCategoryActiveCommandHandler : ICommandHandler<SetCategoryActiveCommand>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetCategoryActiveCommandHandler(
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetCategoryActiveCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        
        if (category is null)
        {
            return Result.Failure(Error.NotFound("Category", request.CategoryId));
        }

        if (request.IsActive)
            category.Activate();
        else
            category.Deactivate();
        
        _categoryRepository.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
