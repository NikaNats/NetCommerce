using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Inventory.Application.Stock.Commands;

/// <summary>
///     Command to create a new stock record for a product.
/// </summary>
public record CreateStockCommand(
    Guid ProductId,
    string Sku,
    int InitialQuantity,
    int LowStockThreshold = 10,
    string? WarehouseLocation = null) : ICommand<Guid>;

public sealed class CreateStockCommandHandler : ICommandHandler<CreateStockCommand, Guid>
{
    private readonly IStockRepository _stockRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateStockCommandHandler(
        IStockRepository stockRepository,
        IUnitOfWork unitOfWork)
    {
        _stockRepository = stockRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateStockCommand request, CancellationToken cancellationToken)
    {
        // Check if stock already exists for this product
        var existing = await _stockRepository.GetByProductIdAsync(request.ProductId, cancellationToken);
        if (existing is not null)
            return Result.Failure<Guid>(
                Error.Conflict($"Stock already exists for product {request.ProductId}"));

        var stock = Domain.Stock.Stock.Create(
            request.ProductId,
            request.Sku,
            request.InitialQuantity,
            request.LowStockThreshold,
            request.WarehouseLocation);

        await _stockRepository.AddAsync(stock, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return stock.Id;
    }
}

/// <summary>
///     Command to reserve stock for an order.
///     Uses distributed locking to prevent overselling.
/// </summary>
public record ReserveStockCommand(
    Guid ProductId,
    Guid OrderId,
    int Quantity) : ICommand<Guid>;

public sealed class ReserveStockCommandHandler : ICommandHandler<ReserveStockCommand, Guid>
{
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(500);
    private readonly IDistributedLockService _lockService;
    private readonly IStockRepository _stockRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReserveStockCommandHandler(
        IStockRepository stockRepository,
        IDistributedLockService lockService,
        IUnitOfWork unitOfWork)
    {
        _stockRepository = stockRepository;
        _lockService = lockService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(
        ReserveStockCommand request,
        CancellationToken cancellationToken)
    {
        var lockResource = $"stock:reserve:{request.ProductId}";

        // Acquire distributed lock using Redis Redlock
        await using var distributedLock = await _lockService.TryAcquireLockAsync(
            lockResource,
            LockExpiry,
            LockWait,
            LockRetry,
            cancellationToken);

        if (distributedLock is null || !distributedLock.IsAcquired)
            return Result.Failure<Guid>(
                Error.Conflict("Unable to acquire lock. The product is being processed by another request."));

        var stock = await _stockRepository.GetByProductIdAsync(request.ProductId, cancellationToken);

        if (stock is null)
            return Result.Failure<Guid>(
                Error.NotFound("Stock", request.ProductId));

        if (stock.AvailableQuantity < request.Quantity)
            return Result.Failure<Guid>(
                Error.Conflict(
                    $"Insufficient stock. Available: {stock.AvailableQuantity}, Requested: {request.Quantity}"));

        try
        {
            var reservation = stock.Reserve(request.OrderId, request.Quantity);

            _stockRepository.Update(stock);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return reservation.Id;
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<Guid>(Error.Conflict(ex.Message));
        }
    }
}

/// <summary>
///     Command to update stock quantity.
/// </summary>
public record UpdateStockQuantityCommand(
    Guid StockId,
    int QuantityDelta,
    string Reason) : ICommand;

public sealed class UpdateStockQuantityCommandHandler : ICommandHandler<UpdateStockQuantityCommand>
{
    private readonly IStockRepository _stockRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateStockQuantityCommandHandler(
        IStockRepository stockRepository,
        IUnitOfWork unitOfWork)
    {
        _stockRepository = stockRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        UpdateStockQuantityCommand request,
        CancellationToken cancellationToken)
    {
        var stock = await _stockRepository.GetByIdAsync(request.StockId, cancellationToken);

        if (stock is null) return Result.Failure(Error.NotFound("Stock", request.StockId));

        if (request.QuantityDelta > 0)
            stock.AddStock(request.QuantityDelta, request.Reason);
        else if (request.QuantityDelta < 0) stock.RemoveStock(Math.Abs(request.QuantityDelta), request.Reason);

        _stockRepository.Update(stock);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
///     Command to confirm a stock reservation after payment.
/// </summary>
public record ConfirmReservationCommand(
    Guid ProductId,
    Guid ReservationId) : ICommand;

public sealed class ConfirmReservationCommandHandler : ICommandHandler<ConfirmReservationCommand>
{
    private readonly IStockRepository _stockRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmReservationCommandHandler(
        IStockRepository stockRepository,
        IUnitOfWork unitOfWork)
    {
        _stockRepository = stockRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        ConfirmReservationCommand request,
        CancellationToken cancellationToken)
    {
        var stock = await _stockRepository.GetByProductIdAsync(request.ProductId, cancellationToken);

        if (stock is null) return Result.Failure(Error.NotFound("Stock", request.ProductId));

        try
        {
            stock.ConfirmReservation(request.ReservationId);

            _stockRepository.Update(stock);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }
    }
}

/// <summary>
///     Command to release a stock reservation.
/// </summary>
public record ReleaseReservationCommand(
    Guid ProductId,
    Guid ReservationId) : ICommand;

public sealed class ReleaseReservationCommandHandler : ICommandHandler<ReleaseReservationCommand>
{
    private readonly IStockRepository _stockRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReleaseReservationCommandHandler(
        IStockRepository stockRepository,
        IUnitOfWork unitOfWork)
    {
        _stockRepository = stockRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        ReleaseReservationCommand request,
        CancellationToken cancellationToken)
    {
        var stock = await _stockRepository.GetByProductIdAsync(request.ProductId, cancellationToken);

        if (stock is null) return Result.Failure(Error.NotFound("Stock", request.ProductId));

        stock.ReleaseReservation(request.ReservationId);

        _stockRepository.Update(stock);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}