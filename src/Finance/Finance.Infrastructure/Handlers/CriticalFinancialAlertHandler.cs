#nullable enable
using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Kernel.Application.Notifications;
using Wolverine;
using Wolverine.Attributes;

namespace NetCommerce.Finance.Infrastructure.Handlers;

/// <summary>
///     Handles CriticalFinancialAlert events (ghost charges, amount mismatches, etc.)
///
///     <para>
///     <b>Go-Live Requirement:</b> "Will I know about a Ghost Charge before the customer calls support?"
///     This handler ensures finance team is alerted immediately when reconciliation detects anomalies.
///     </para>
///
///     <para>
///     <b>Alert Channels:</b>
///     - Email to finance-alerts distribution list
///     - Logged at CRITICAL level for SIEM/alerting integration (PagerDuty, Datadog, etc.)
///     - Structured logging enables Seq/Kibana dashboards
///     </para>
/// </summary>
[WolverineHandler]
public static class CriticalFinancialAlertHandler
{
    /// <summary>
    ///     Handles ghost charge and other critical financial alerts.
    ///     Sends immediate notification to finance team and logs for monitoring systems.
    /// </summary>
    public static async Task Handle(
        CriticalFinancialAlert alert,
        IEmailProvider emailProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // 1. CRITICAL LOG: SIEM/Monitoring Integration
        // ═══════════════════════════════════════════════════════════════════════════
        // This log entry should trigger PagerDuty/Datadog/Splunk alerts
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["alert_type"] = "GHOST_CHARGE",
            ["external_txn_id"] = alert.ExternalTransactionId,
            ["amount"] = alert.Amount,
            ["severity"] = "CRITICAL"
        }))
        {
            logger.LogCritical(
                "🚨 CRITICAL FINANCIAL ALERT: Ghost charge detected! " +
                "ExternalTxnId={ExternalTxnId}, Amount={Amount:C}, Reason={Reason}",
                alert.ExternalTransactionId,
                alert.Amount,
                alert.Reason);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // 2. EMAIL NOTIFICATION: Finance Team Alert
        // ═══════════════════════════════════════════════════════════════════════════
        var subject = $"🚨 CRITICAL: Ghost Charge Detected - ${alert.Amount:N2}";

        var htmlBody = $"""
            <html>
            <body style="font-family: Arial, sans-serif; padding: 20px;">
                <div style="background-color: #ff4444; color: white; padding: 15px; border-radius: 5px;">
                    <h1 style="margin: 0;">⚠️ Critical Financial Alert</h1>
                </div>

                <div style="margin-top: 20px; padding: 15px; border: 1px solid #ddd; border-radius: 5px;">
                    <h2 style="color: #333;">Ghost Charge Detected</h2>

                    <table style="width: 100%; border-collapse: collapse;">
                        <tr>
                            <td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>External Transaction ID:</strong></td>
                            <td style="padding: 8px; border-bottom: 1px solid #eee; font-family: monospace;">{alert.ExternalTransactionId}</td>
                        </tr>
                        <tr>
                            <td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>Amount:</strong></td>
                            <td style="padding: 8px; border-bottom: 1px solid #eee; color: #ff4444; font-weight: bold;">${alert.Amount:N2}</td>
                        </tr>
                        <tr>
                            <td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>Reason:</strong></td>
                            <td style="padding: 8px; border-bottom: 1px solid #eee;">{alert.Reason}</td>
                        </tr>
                        <tr>
                            <td style="padding: 8px; border-bottom: 1px solid #eee;"><strong>Detected At:</strong></td>
                            <td style="padding: 8px; border-bottom: 1px solid #eee;">{alert.OccurredOn:yyyy-MM-dd HH:mm:ss} UTC</td>
                        </tr>
                    </table>
                </div>

                <div style="margin-top: 20px; padding: 15px; background-color: #fff3cd; border-radius: 5px;">
                    <h3 style="color: #856404; margin-top: 0;">🔍 Immediate Actions Required:</h3>
                    <ol>
                        <li>Check Stripe Dashboard for transaction: <code>{alert.ExternalTransactionId}</code></li>
                        <li>Verify if this is a legitimate charge or potential fraud</li>
                        <li>If fraud, initiate refund and notify security team</li>
                        <li>Document findings in the reconciliation session</li>
                    </ol>
                </div>

                <div style="margin-top: 20px; font-size: 12px; color: #666;">
                    <p>This is an automated alert from NetCommerce Reconciliation Engine.</p>
                    <p>Event ID: {alert.EventId}</p>
                </div>
            </body>
            </html>
            """;

        try
        {
            // Send to finance alerts distribution list
            // In production, configure via appsettings: Finance:AlertRecipients
            await emailProvider.SendEmailAsync(
                to: "finance-alerts@company.com",
                subject: subject,
                htmlBody: htmlBody,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Finance alert email sent for ghost charge {ExternalTxnId}",
                alert.ExternalTransactionId);
        }
        catch (Exception ex)
        {
            // Email failure should NOT prevent the alert from being logged
            // The CRITICAL log above will still trigger monitoring systems
            logger.LogError(ex,
                "Failed to send finance alert email for {ExternalTxnId}. " +
                "Alert was still logged at CRITICAL level for monitoring systems.",
                alert.ExternalTransactionId);
        }
    }
}
