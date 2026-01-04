using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.SharedKernel.Events;
using Wolverine;

namespace NetCommerce.Payments.Infrastructure.BackgroundJobs;

/// <summary>
/// Background job that reconciles pending payments with payment provider status.
///
/// WEBHOOK-FIRST PATTERN - SAFETY NET
/// - Webhooks can fail or be delayed
/// - This job polls payment provider API for payments stuck in Pending >10 minutes
/// - If provider shows "succeeded" but webhook never arrived, triggers manual confirmation
///
/// Prevents permanent "stuck" orders when webhooks are lost.
/// </summary>
public class PaymentReconciliationJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PaymentReconciliationJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5); // Run every 5 minutes
    private readonly TimeSpan _pendingThreshold = TimeSpan.FromMinutes(10); // Check payments >10 minutes old

    public PaymentReconciliationJob(
        IServiceProvider services,
        ILogger<PaymentReconciliationJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Payment Reconciliation Job started. Running every {Interval} minutes.", _interval.TotalMinutes);

        // Wait 30 seconds before first run (give app time to start)
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcilePendingPaymentsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in payment reconciliation job");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Payment Reconciliation Job stopped");
    }

    private async Task ReconcilePendingPaymentsAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentTransactionRepository>();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var olderThan = DateTime.UtcNow - _pendingThreshold;

        try
        {
            // Find payments stuck in Pending for >10 minutes
            var stuckPayments = await repository.GetPendingPaymentsAsync(olderThan, cancellationToken);

            if (stuckPayments.Count == 0)
            {
                _logger.LogDebug("Reconciliation: No stuck payments found");
                return;
            }

            _logger.LogWarning(
                "Reconciliation: Found {Count} payments stuck in Pending status for >{Threshold} minutes",
                stuckPayments.Count,
                _pendingThreshold.TotalMinutes);

            foreach (var payment in stuckPayments)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await ReconcilePaymentAsync(payment, gateway, bus, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying pending payments for reconciliation");
        }
    }

    private async Task ReconcilePaymentAsync(
        PaymentTransaction payment,
        IPaymentGateway gateway,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(payment.ExternalTransactionId))
            {
                _logger.LogWarning(
                    "Payment {PaymentId} for Order {OrderId} has no ExternalTransactionId. " +
                    "Cannot reconcile. Age: {Age} minutes",
                    payment.Id,
                    payment.OrderId,
                    (DateTime.UtcNow - payment.CreatedAt).TotalMinutes);
                return;
            }

            // Query payment provider API for current status
            var statusResult = await gateway.GetPaymentStatusAsync(
                payment.ExternalTransactionId,
                cancellationToken);

            if (statusResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to query payment status for Payment {PaymentId}, ExternalId: {ExternalId}. " +
                    "Error: {Error}",
                    payment.Id,
                    payment.ExternalTransactionId,
                    statusResult.Error?.Description);
                return;
            }

            var currentStatus = statusResult.Value.Status;

            // If succeeded but webhook never arrived, trigger manual confirmation
            if (currentStatus == PaymentResultStatus.Succeeded)
            {
                _logger.LogWarning(
                    "Reconciliation: Payment {PaymentId} for Order {OrderId} succeeded but webhook missed. " +
                    "ExternalTransactionId: {ExternalId}. Processing now. Age: {Age} minutes",
                    payment.Id,
                    payment.OrderId,
                    payment.ExternalTransactionId,
                    (DateTime.UtcNow - payment.CreatedAt).TotalMinutes);

                await bus.InvokeAsync(new ProcessExternalPaymentConfirmation(
                    ExternalTransactionId: payment.ExternalTransactionId,
                    Status: "Succeeded",
                    WebhookEventId: $"reconciliation_{DateTime.UtcNow.Ticks}"
                ), cancellationToken);
            }
            // If failed, trigger failure confirmation
            else if (currentStatus == PaymentResultStatus.Failed)
            {
                _logger.LogWarning(
                    "Reconciliation: Payment {PaymentId} for Order {OrderId} failed but webhook missed. " +
                    "ExternalTransactionId: {ExternalId}. Processing now.",
                    payment.Id,
                    payment.OrderId,
                    payment.ExternalTransactionId);

                await bus.InvokeAsync(new ProcessExternalPaymentConfirmation(
                    ExternalTransactionId: payment.ExternalTransactionId,
                    Status: "Failed",
                    WebhookEventId: $"reconciliation_{DateTime.UtcNow.Ticks}"
                ), cancellationToken);
            }
            // If still pending, log but don't act (wait longer)
            else if (currentStatus == PaymentResultStatus.Pending)
            {
                _logger.LogInformation(
                    "Reconciliation: Payment {PaymentId} for Order {OrderId} still pending at provider. " +
                    "Age: {Age} minutes. Will check again in next cycle.",
                    payment.Id,
                    payment.OrderId,
                    (DateTime.UtcNow - payment.CreatedAt).TotalMinutes);
            }
            // If requires action (3D Secure), log
            else if (currentStatus == PaymentResultStatus.RequiresAction)
            {
                _logger.LogInformation(
                    "Reconciliation: Payment {PaymentId} for Order {OrderId} requires customer action (3D Secure). " +
                    "Age: {Age} minutes.",
                    payment.Id,
                    payment.OrderId,
                    (DateTime.UtcNow - payment.CreatedAt).TotalMinutes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error reconciling Payment {PaymentId}, ExternalId: {ExternalId}",
                payment.Id,
                payment.ExternalTransactionId);
        }
    }
}
