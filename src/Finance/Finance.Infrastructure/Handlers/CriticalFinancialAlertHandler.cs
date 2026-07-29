#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Audit;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Kernel.Application.Notifications;
using System.Net.Http.Json;
using System.Text.Json;
using Wolverine;
using Wolverine.Attributes;

namespace NetCommerce.Finance.Infrastructure.Handlers;

/// <summary>
///     Handles CriticalFinancialAlert events (ghost charges, amount mismatches, disputes, etc.)
///
///     <para>
///     <b>Go-Live Requirement:</b> "Will I know about a Ghost Charge before the customer calls support?"
///     This handler ensures finance team is alerted immediately when reconciliation detects anomalies.
///     </para>
///
///     <para>
///     <b>Alert Channels:</b>
///     1. PagerDuty — Real-time on-call alerting with escalation
///     2. Email to finance-alerts distribution list
///     3. CRITICAL log level for SIEM/alerting integration (Datadog, Seq, etc.)
///     4. Audit log entry for compliance
///     </para>
/// </summary>
[Transactional(typeof(FinanceDbContext))]
public static class CriticalFinancialAlertHandler
{
    /// <summary>
    ///     Handles ghost charge and other critical financial alerts.
    ///     Sends immediate notification via multiple channels for redundancy.
    /// </summary>
    public static async Task Handle(
        CriticalFinancialAlert alert,
        IEmailProvider emailProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<AlertingOptions> alertingOptions,
        IFinancialAuditRepository auditRepository,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var options = alertingOptions.Value;

        // ═══════════════════════════════════════════════════════════════════════════
        // 1. AUDIT LOG: Immutable record for compliance
        // ═══════════════════════════════════════════════════════════════════════════
        var auditEntry = FinancialAuditEntry.Create(
            FinancialAuditType.AlertTriggered,
            "Alert",
            alert.EventId.ToString(),
            "System",
            ActorType.System,
            $"Critical financial alert: {alert.Reason}",
            externalTransactionId: alert.ExternalTransactionId,
            amount: alert.Amount);

        await auditRepository.AppendAsync(auditEntry, cancellationToken);

        // ═══════════════════════════════════════════════════════════════════════════
        // 2. CRITICAL LOG: SIEM/Monitoring Integration
        // ═══════════════════════════════════════════════════════════════════════════
        // This log entry should trigger PagerDuty/Datadog/Splunk alerts via log-based alerting
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["alert_type"] = "CRITICAL_FINANCIAL",
            ["external_txn_id"] = alert.ExternalTransactionId,
            ["amount"] = alert.Amount,
            ["severity"] = "CRITICAL"
        }))
        {
            logger.LogCritical(
                "🚨 CRITICAL FINANCIAL ALERT: {Reason}. " +
                "ExternalTxnId={ExternalTxnId}, Amount={Amount:C}",
                alert.Reason,
                alert.ExternalTransactionId,
                alert.Amount);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // 3. PAGERDUTY: Real-time on-call alerting
        // ═══════════════════════════════════════════════════════════════════════════
        if (!string.IsNullOrEmpty(options.PagerDutyRoutingKey))
        {
            await SendPagerDutyAlertAsync(
                httpClientFactory,
                options.PagerDutyRoutingKey,
                alert,
                logger,
                cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // 4. EMAIL NOTIFICATION: Finance Team Alert
        // ═══════════════════════════════════════════════════════════════════════════
        if (options.SendEmailAlerts)
        {
            await SendEmailAlertAsync(
                emailProvider,
                options.FinanceAlertEmail,
                alert,
                logger,
                cancellationToken);
        }
    }

    private static async Task SendPagerDutyAlertAsync(
        IHttpClientFactory httpClientFactory,
        string routingKey,
        CriticalFinancialAlert alert,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("PagerDuty");

            // PagerDuty Events API v2 payload
            // See: https://developer.pagerduty.com/docs/events-api-v2/trigger-events/
            var payload = new
            {
                routing_key = routingKey,
                event_action = "trigger",
                dedup_key = $"netcommerce-finance-{alert.ExternalTransactionId}",
                payload = new
                {
                    summary = $"Critical Financial Alert: {alert.Reason}",
                    source = "NetCommerce Finance Module",
                    severity = "critical",
                    timestamp = alert.OccurredOn.ToString("o"),
                    custom_details = new
                    {
                        external_transaction_id = alert.ExternalTransactionId,
                        amount = alert.Amount,
                        reason = alert.Reason,
                        event_id = alert.EventId
                    }
                },
                links = new[]
                {
                    new
                    {
                        href = $"https://dashboard.stripe.com/search?query={alert.ExternalTransactionId}",
                        text = "View in Stripe Dashboard"
                    }
                }
            };

            var response = await client.PostAsJsonAsync("enqueue", payload, ct);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "PagerDuty alert sent successfully for {ExternalTxnId}",
                    alert.ExternalTransactionId);
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning(
                    "PagerDuty alert failed: {StatusCode} - {Body}",
                    response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            // PagerDuty failure should NOT prevent other alert channels
            logger.LogError(ex,
                "Failed to send PagerDuty alert for {ExternalTxnId}. " +
                "Alert was still logged at CRITICAL level and audit trail created.",
                alert.ExternalTransactionId);
        }
    }

    private static async Task SendEmailAlertAsync(
        IEmailProvider emailProvider,
        string recipientEmail,
        CriticalFinancialAlert alert,
        ILogger logger,
        CancellationToken ct)
    {
        var subject = $"🚨 CRITICAL: Financial Alert - ${alert.Amount:N2}";

        var htmlBody = $"""
            <html>
            <body style="font-family: Arial, sans-serif; padding: 20px;">
                <div style="background-color: #ff4444; color: white; padding: 15px; border-radius: 5px;">
                    <h1 style="margin: 0;">⚠️ Critical Financial Alert</h1>
                </div>

                <div style="margin-top: 20px; padding: 15px; border: 1px solid #ddd; border-radius: 5px;">
                    <h2 style="color: #333;">Financial Discrepancy Detected</h2>

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
            await emailProvider.SendEmailAsync(
                to: recipientEmail,
                subject: subject,
                htmlBody: htmlBody,
                cancellationToken: ct);

            logger.LogInformation(
                "Finance alert email sent to {Recipient} for {ExternalTxnId}",
                recipientEmail,
                alert.ExternalTransactionId);
        }
        catch (Exception ex)
        {
            // Email failure should NOT prevent other alert channels
            logger.LogError(ex,
                "Failed to send finance alert email for {ExternalTxnId}. " +
                "PagerDuty and CRITICAL log alerts are independent.",
                alert.ExternalTransactionId);
        }
    }
}
