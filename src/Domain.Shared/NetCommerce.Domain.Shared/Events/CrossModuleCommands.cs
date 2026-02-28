#nullable enable

using NetCommerce.Kernel.Application;

namespace NetCommerce.Domain.Shared.Events;

/// <summary>
///     Cross-module command: Create a shadow order during financial reconciliation.
///     Shadow orders account for "ghost charges" — payments that exist in the PSP
///     but have no corresponding internal order record.
///     Sent from Finance module to Ordering module via Wolverine messaging.
///     Note: Audit logging is handled by the receiving handler in Ordering,
///     not via IAuditableCommand (which lives in Kernel.Compliance, a layer
///     Domain.Shared should not depend on).
/// </summary>
public record CreateShadowOrderCommand(
    string ExternalTransactionId,
    decimal Amount,
    string Currency,
    string ResolvedBy,
    string Reason) : ICommand<Guid>;
