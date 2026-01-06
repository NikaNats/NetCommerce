using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Finance.Application.Services;
using Wolverine.Attributes;

namespace NetCommerce.Finance.Infrastructure.Handlers;

/// <summary>
///     Handler for scheduled daily reconciliation.
///     Runs reconciliation for "yesterday" to ensure PSP settlement is complete.
/// </summary>
[WolverineHandler]
public static class ReconciliationSchedulerHandler
{
    /// <summary>
    ///     Handle daily reconciliation command.
    ///     Scheduled via cron or called manually by admin.
    /// </summary>
    [Transactional]
    public static async Task Handle(
        CheckDailyReconciliation command,
        ReconciliationEngine engine,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing daily reconciliation for {Date}", command.Date.ToShortDateString());

        try
        {
            await engine.ReconcileDailyAsync(command.Date, cancellationToken);
            logger.LogInformation("Daily reconciliation completed successfully for {Date}", command.Date);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily reconciliation failed for {Date}", command.Date);
            throw; // Let Wolverine handle retries/dead letters
        }
    }
}
