namespace NetCommerce.Finance.Application.Commands;

/// <summary>
///     Command to trigger daily reconciliation.
///     Scheduled via Wolverine cron or called manually.
/// </summary>
public record CheckDailyReconciliation(DateTime Date);
