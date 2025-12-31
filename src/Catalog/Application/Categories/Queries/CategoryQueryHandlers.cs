using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Catalog.Application.Categories.Mappers;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Catalog.Application.Categories.Queries;

public sealed class GetAllCategoriesQueryHandler : IQueryHandler<GetAllCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryMapper _mapper;

    public GetAllCategoriesQueryHandler(
        ICategoryRepository categoryRepository,
        ICategoryMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(
        GetAllCategoriesQuery request, 
        CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetAllAsync(cancellationToken);
        return Result.Success(_mapper.MapToDto(categories));
    }
}

public sealed class GetCategoryByIdQueryHandler : IQueryHandler<GetCategoryByIdQuery, CategoryDto>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryMapper _mapper;

    public GetCategoryByIdQueryHandler(
        ICategoryRepository categoryRepository,
        ICategoryMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<Result<CategoryDto>> Handle(
        GetCategoryByIdQuery request, 
        CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.Id, cancellationToken);
        
        if (category is null)
        {
            return Result.Failure<CategoryDto>(Error.NotFound("Category", request.Id));
        }

        return _mapper.MapToDto(category);
    }
}

public sealed class GetCategoryBySlugQueryHandler : IQueryHandler<GetCategoryBySlugQuery, CategoryDto>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryMapper _mapper;

    public GetCategoryBySlugQueryHandler(
        ICategoryRepository categoryRepository,
        ICategoryMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<Result<CategoryDto>> Handle(
        GetCategoryBySlugQuery request, 
        CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetBySlugAsync(request.Slug, cancellationToken);
        
        if (category is null)
        {
            return Result.Failure<CategoryDto>(
                Error.NotFound("Category", $"slug:{request.Slug}"));
        }

        return _mapper.MapToDto(category);
    }
}

public sealed class GetRootCategoriesQueryHandler : IQueryHandler<GetRootCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryMapper _mapper;

    public GetRootCategoriesQueryHandler(
        ICategoryRepository categoryRepository,
        ICategoryMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(
        GetRootCategoriesQuery request, 
        CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetRootCategoriesAsync(cancellationToken);
        return Result.Success(_mapper.MapToDto(categories));
    }
}

public sealed class GetChildCategoriesQueryHandler : IQueryHandler<GetChildCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryMapper _mapper;

    public GetChildCategoriesQueryHandler(
        ICategoryRepository categoryRepository,
        ICategoryMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(
        GetChildCategoriesQuery request, 
        CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetChildCategoriesAsync(request.ParentId, cancellationToken);
        return Result.Success(_mapper.MapToDto(categories));
    }
}
