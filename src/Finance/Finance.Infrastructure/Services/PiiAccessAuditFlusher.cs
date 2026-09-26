#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Infrastructure.Persistence;

namespace NetCommerce.Finance.Infrastructure.Services;

/// <summary>
///     Batches PII access-audit timestamps queued in <see cref="PiiAccessAuditChannel"/>
///     into a single bulk UPDATE per flush interval. Keeps
///     <c>FindByProfileIdAsync</c> purely read-only (no row locks, no write
///     contention on <c>finance.pii_vault_entries</c>).
/// </summary>
public sealed class PiiAccessAuditFlusher : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private const int MaxBatchSize = 1000;

    private readonly PiiAccessAuditChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PiiAccessAuditFlusher> _logger;

    public PiiAccessAuditFlusher(
        PiiAccessAuditChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<PiiAccessAuditFlusher> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FlushBatchAsync(stoppingToken);
        }
    }

    private async Task FlushBatchAsync(CancellationToken stoppingToken)
    {
        var batch = new HashSet<Guid>();
        while (batch.Count < MaxBatchSize && _channel.TryRead(out var profileId))
        {
            batch.Add(profileId);
        }

        if (batch.Count == 0)
            return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var now = DateTime.UtcNow;

            await db.PiiVaultEntries
                .Where(e => batch.Contains(e.ProfileId))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.LastAccessedAt, now),
                    stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush PII access-audit batch of {Count} entries.", batch.Count);
        }
    }
}
