#nullable enable
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NetCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Health check that reflects ReservationCleanupJob state.
/// Unhealthy when circuit breaker tripped (ConsecutiveFailures >= 3).
/// </summary>
public sealed class CleanupJobHealthCheck : IHealthCheck
{
    private readonly CleanupJobHealthState _state;
    public CleanupJobHealthCheck(CleanupJobHealthState state) => _state = state;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_state.IsDegraded)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Reservation cleanup is failing",
                data: new Dictionary<string, object>
                {
                    ["consecutiveFailures"] = _state.ConsecutiveFailures,
                    ["lastError"] = _state.LastError ?? "Unknown",
                    ["lastSuccessUtc"] = _state.LastSuccessUtc?.ToString("O") ?? "never"
                }));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Reservation cleanup healthy",
            data: new Dictionary<string, object>
            {
                ["consecutiveFailures"] = _state.ConsecutiveFailures,
                ["lastSuccessUtc"] = _state.LastSuccessUtc?.ToString("O") ?? "never"
            }));
    }
}
