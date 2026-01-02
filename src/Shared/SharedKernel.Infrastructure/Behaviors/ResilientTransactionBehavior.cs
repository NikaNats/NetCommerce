using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Application;

namespace NetCommerce.SharedKernel.Infrastructure.Behaviors;

public class ResilientTransactionBehavior<TRequest, TResponse, TDbContext> 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly ILogger<ResilientTransactionBehavior<TRequest, TResponse, TDbContext>> _logger;

    public ResilientTransactionBehavior(
        TDbContext dbContext,
        ILogger<ResilientTransactionBehavior<TRequest, TResponse, TDbContext>> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    private static class ModuleMatcher
    {
        // Computed once per closed generic type combination
        public static readonly bool IsMatch = ComputeMatch();

        private static bool ComputeMatch()
        {
            var requestModule = typeof(TRequest).Namespace?.Split('.')[1] ?? "";
            var contextModule = typeof(TDbContext).Namespace?.Split('.')[1] ?? "";
            return string.Equals(requestModule, contextModule, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        // --- GUARD CLAUSE: Prevent Cross-Module Transaction Opening ---
        // Optimization: Use cached result from ModuleMatcher
        if (!ModuleMatcher.IsMatch)
        {
            return await next();
        }
        // -------------------------------------------------------------

        var typeName = request.GetType().Name;

        try
        {
            // If there's already a transaction, just continue (support nested scopes)
            if (_dbContext.Database.CurrentTransaction != null)
            {
                return await next();
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                // Clear tracker to prevent "Entity already tracked" errors on retry
                _dbContext.ChangeTracker.Clear();

                // Begin Transaction inside the strategy
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
                
                _logger.LogInformation("Begin transaction {TransactionId} for {CommandName}", 
                    transaction.TransactionId, typeName);

                // Execute the Command Handler logic (Business Logic + DbContext.Add/Update)
                var response = await next();

                // If result is failure (Result Pattern), do NOT commit
                var isFailure = false;
                if (response is NetCommerce.SharedKernel.Results.Result result)
                {
                    isFailure = result.IsFailure;
                }
                
                if (isFailure)
                {
                     // Do nothing, transaction disposes and rolls back
                     _logger.LogWarning("Transaction {TransactionId} for {CommandName} rolled back due to domain failure", 
                        transaction.TransactionId, typeName);
                     return response;
                }

                _logger.LogInformation("Commit transaction {TransactionId} for {CommandName}", 
                    transaction.TransactionId, typeName);

                // Commit Transaction
                await _dbContext.SaveChangesAsync(cancellationToken); 
                await transaction.CommitAsync(cancellationToken);
                
                return response;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling transaction for {CommandName}", typeName);
            throw;
        }
    }
}
