using NetCommerce.Catalog.Domain.Products;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Catalog.Application.Products.Commands;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateProductCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(
        CreateProductCommand request, 
        CancellationToken cancellationToken)
    {
        // Check if SKU already exists
        if (await _productRepository.ExistsAsync(request.Sku, cancellationToken))
        {
            return Result.Failure<Guid>(
                Error.Conflict($"Product with SKU '{request.Sku}' already exists"));
        }

        var price = Money.Create(request.Price, request.Currency);
        
        var product = Product.Create(
            request.Name,
            request.Description,
            request.Sku,
            price,
            request.CategoryId);

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return product.Id;
    }
}

public sealed class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProductCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        UpdateProductCommand request, 
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        
        if (product is null)
        {
            return Result.Failure(Error.NotFound(nameof(Product), request.ProductId));
        }

        product.UpdateDetails(request.Name, request.Description, request.Sku);
        
        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class UpdateProductPriceCommandHandler : ICommandHandler<UpdateProductPriceCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProductPriceCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        UpdateProductPriceCommand request, 
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        
        if (product is null)
        {
            return Result.Failure(Error.NotFound(nameof(Product), request.ProductId));
        }

        var newPrice = Money.Create(request.NewPrice, request.Currency);
        product.UpdatePrice(newPrice);
        
        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class PublishProductCommandHandler : ICommandHandler<PublishProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PublishProductCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        PublishProductCommand request, 
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        
        if (product is null)
        {
            return Result.Failure(Error.NotFound(nameof(Product), request.ProductId));
        }

        product.Publish();
        
        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class AddProductImageCommandHandler : ICommandHandler<AddProductImageCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AddProductImageCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        AddProductImageCommand request, 
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        
        if (product is null)
        {
            return Result.Failure(Error.NotFound(nameof(Product), request.ProductId));
        }

        product.AddImage(request.ImageKey, request.DisplayOrder, request.IsPrimary);
        
        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
