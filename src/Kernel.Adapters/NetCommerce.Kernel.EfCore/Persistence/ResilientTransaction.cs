#nullable enable
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Resilient transaction wrapper for EF Core with retry support.
/// </summary>
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
    ///     the entire block is re-executed.
    /// </summary>
    public async Task ExecuteAsync(Func<Task> action)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            try
            {
                await action();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    /// <summary>
    ///     Executes a delegate inside a resilient transaction and returns a result.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            try
            {
                var result = await action();
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }
}
