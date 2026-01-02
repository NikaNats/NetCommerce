using System.Data;
using Microsoft.EntityFrameworkCore;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence;

public sealed class ResilientTransaction
{
    private readonly DbContext _context;

    private ResilientTransaction(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public static ResilientTransaction New(DbContext context)
    {
        return new ResilientTransaction(context);
    }

    /// <summary>
    ///     Executes a delegate inside a resilient transaction.
    ///     If a transient failure occurs during the transaction commit,
    ///     the entire block (including the business logic) is re-executed.
    /// </summary>
    public async Task ExecuteAsync(Func<Task> action)
    {
        // Retrieves the Execution Strategy (e.g., NpgsqlRetryingExecutionStrategy)
        var strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            // Clear tracker to prevent "Entity already tracked" errors on retry
            _context.ChangeTracker.Clear();

            // IMPORTANT: We must open the transaction manually inside the strategy
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            try
            {
                await action();
                await transaction.CommitAsync();
            }
            catch
            {
                // Rollback is handled automatically by Dispose, 
                // but explicit rollback can be safer in some edge cases.
                await transaction.RollbackAsync();
                throw;
            }
        });
    }
}