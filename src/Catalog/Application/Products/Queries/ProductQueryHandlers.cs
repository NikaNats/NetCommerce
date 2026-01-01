using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Mappers;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Catalog.Application.Products.Queries;

public sealed class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IProductMapper _mapper;
    private readonly IProductRepository _productRepository;

    public GetProductByIdQueryHandler(
        IProductRepository productRepository,
        IProductMapper mapper)
    {
        _productRepository = productRepository;
        _mapper = mapper;
    }

    public async Task<Result<ProductDto>> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<ProductDto>(
                Error.NotFound(nameof(Product), request.ProductId));

        return _mapper.MapToDto(product);
    }
}

public sealed class GetProductBySlugQueryHandler : IQueryHandler<GetProductBySlugQuery, ProductDto>
{
    private readonly IProductMapper _mapper;
    private readonly IProductRepository _productRepository;

    public GetProductBySlugQueryHandler(
        IProductRepository productRepository,
        IProductMapper mapper)
    {
        _productRepository = productRepository;
        _mapper = mapper;
    }

    public async Task<Result<ProductDto>> Handle(
        GetProductBySlugQuery request,
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetBySlugAsync(request.Slug, cancellationToken);

        if (product is null)
            return Result.Failure<ProductDto>(
                Error.NotFound(nameof(Product), request.Slug));

        return _mapper.MapToDto(product);
    }
}