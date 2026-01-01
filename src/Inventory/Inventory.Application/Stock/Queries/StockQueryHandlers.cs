using NetCommerce.Inventory.Application.Stock.Mappers;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Inventory.Application.Stock.Queries;

public sealed class GetStockByProductIdQueryHandler : IQueryHandler<GetStockByProductIdQuery, StockDto>
{
    private readonly IStockMapper _mapper;
    private readonly IStockRepository _stockRepository;

    public GetStockByProductIdQueryHandler(
        IStockRepository stockRepository,
        IStockMapper mapper)
    {
        _stockRepository = stockRepository;
        _mapper = mapper;
    }

    public async Task<Result<StockDto>> Handle(
        GetStockByProductIdQuery request,
        CancellationToken cancellationToken)
    {
        var stock = await _stockRepository.GetByProductIdAsync(request.ProductId, cancellationToken);

        if (stock is null) return Result.Failure<StockDto>(Error.NotFound("Stock", request.ProductId));

        return _mapper.MapToDto(stock);
    }
}

public sealed class GetLowStockItemsQueryHandler : IQueryHandler<GetLowStockItemsQuery, IReadOnlyList<StockDto>>
{
    private readonly IStockMapper _mapper;
    private readonly IStockRepository _stockRepository;

    public GetLowStockItemsQueryHandler(
        IStockRepository stockRepository,
        IStockMapper mapper)
    {
        _stockRepository = stockRepository;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<StockDto>>> Handle(
        GetLowStockItemsQuery request,
        CancellationToken cancellationToken)
    {
        var stocks = await _stockRepository.GetLowStockItemsAsync(cancellationToken);
        return Result.Success(_mapper.MapToDto(stocks));
    }
}

public sealed class GetStockBySkuQueryHandler : IQueryHandler<GetStockBySkuQuery, StockDto>
{
    private readonly IStockMapper _mapper;
    private readonly IStockRepository _stockRepository;

    public GetStockBySkuQueryHandler(
        IStockRepository stockRepository,
        IStockMapper mapper)
    {
        _stockRepository = stockRepository;
        _mapper = mapper;
    }

    public async Task<Result<StockDto>> Handle(
        GetStockBySkuQuery request,
        CancellationToken cancellationToken)
    {
        var stock = await _stockRepository.GetBySkuAsync(request.Sku, cancellationToken);

        if (stock is null)
            return Result.Failure<StockDto>(
                Error.NotFound("Stock", $"sku:{request.Sku}"));

        return _mapper.MapToDto(stock);
    }
}