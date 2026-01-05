#region

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

#endregion

namespace NetCommerce.Api.Endpoints.Admin;

/// <summary>
///     2025 Operational Recovery API for manual intervention.
///     Key Principle: "A 'ManualInterventionRequired' state in a Saga is a Business Failure, not a code crash.
///     You must provide a way for a human operator to resolve it."
///     These endpoints are used by support engineers when:
///     - Payment succeeds in Stripe but webhook fails (need to manually mark payment as complete)
///     - Inventory reservation times out but items were reserved (need to manually confirm)
///     - Refund is processed manually via Stripe Dashboard (need to close the saga)
/// </summary>
[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin,SupportEngineer")]
public class AdminOrderRecoveryEndpoints : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly ILogger<AdminOrderRecoveryEndpoints> _logger;

    public AdminOrderRecoveryEndpoints(
        IMessageBus bus,
        ILogger<AdminOrderRecoveryEndpoints> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    /// <summary>
    ///     Force-complete an order that is stuck in ManualInterventionRequired state.
    ///     Use Case: Payment succeeded in Stripe, but webhook failed to deliver.
    ///     Support engineer verifies payment in Stripe Dashboard, then calls this endpoint
    ///     to manually move the saga to Completed state.
    /// </summary>
    /// <param name="orderId">Order ID to force-complete</param>
    /// <param name="reason">Reason for manual intervention (audit trail)</param>
    /// <param name="verifiedByUserId">Support engineer who verified the payment</param>
    [HttpPost("{orderId:guid}/force-complete")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForceCompleteSaga(
        [FromRoute] Guid orderId,
        [FromBody] ForceCompleteSagaRequest request)
    {
        _logger.LogWarning(
            "MANUAL INTERVENTION: Force-completing order {OrderId}. Reason: {Reason}. User: {UserId}",
            orderId, request.Reason, User.Identity?.Name);

        var command = new ForceCompleteOrderSagaCommand(
            orderId,
            request.Reason,
            User.Identity?.Name ?? "Unknown",
            DateTimeOffset.UtcNow);

        await _bus.PublishAsync(command);

        return Accepted(new
        {
            OrderId = orderId,
            Message = "Force-complete command sent. Saga will be marked as completed.",
            request.Reason,
            ProcessedBy = User.Identity?.Name
        });
    }

    /// <summary>
    ///     Override payment status when payment was manually verified in Stripe Dashboard.
    ///     Use Case: Customer calls support saying payment went through, but order shows "Payment Failed."
    ///     Support engineer checks Stripe Dashboard, sees successful charge, calls this endpoint.
    /// </summary>
    [HttpPost("{orderId:guid}/override-payment-status")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OverridePaymentStatus(
        [FromRoute] Guid orderId,
        [FromBody] OverridePaymentStatusRequest request)
    {
        _logger.LogWarning(
            "MANUAL INTERVENTION: Overriding payment status for order {OrderId}. " +
            "Status: {Status}, Stripe Charge ID: {ChargeId}, Reason: {Reason}",
            orderId, request.PaymentStatus, request.StripeChargeId, request.Reason);

        var command = new OverridePaymentStatusCommand(
            orderId,
            request.PaymentStatus,
            request.StripeChargeId,
            request.Reason,
            User.Identity?.Name ?? "Unknown");

        await _bus.PublishAsync(command);

        return Accepted(new
        {
            OrderId = orderId,
            NewStatus = request.PaymentStatus,
            ChargeId = request.StripeChargeId,
            Message = "Payment status override sent."
        });
    }

    /// <summary>
    ///     Cancel an order that is stuck in an intermediate state.
    ///     Use Case: Inventory reservation timed out, customer already contacted, order needs to be cancelled.
    ///     Support engineer confirms with customer, then calls this endpoint to cancel and refund.
    /// </summary>
    [HttpPost("{orderId:guid}/force-cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForceCancelOrder(
        [FromRoute] Guid orderId,
        [FromBody] ForceCancelOrderRequest request)
    {
        _logger.LogWarning(
            "MANUAL INTERVENTION: Force-cancelling order {OrderId}. Reason: {Reason}. " +
            "Refund Amount: {RefundAmount}",
            orderId, request.Reason, request.RefundAmount);

        var command = new ForceCancelOrderCommand(
            orderId,
            request.Reason,
            request.RefundAmount,
            request.NotifyCustomer,
            User.Identity?.Name ?? "Unknown");

        await _bus.PublishAsync(command);

        return Accepted(new
        {
            OrderId = orderId,
            request.RefundAmount,
            request.NotifyCustomer,
            Message = "Force-cancel command sent. Order will be cancelled and refunded."
        });
    }

    /// <summary>
    ///     Retry a failed saga step (payment, inventory reservation, shipping label).
    ///     Use Case: Stripe API was down when order was placed, now it's back up.
    ///     Support engineer clicks "Retry Payment" in admin UI, which calls this endpoint.
    /// </summary>
    [HttpPost("{orderId:guid}/retry-step")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RetryFailedStep(
        [FromRoute] Guid orderId,
        [FromBody] RetryStepRequest request)
    {
        _logger.LogInformation(
            "MANUAL INTERVENTION: Retrying saga step for order {OrderId}. Step: {Step}",
            orderId, request.Step);

        var command = new RetrySagaStepCommand(
            orderId,
            request.Step,
            User.Identity?.Name ?? "Unknown");

        await _bus.PublishAsync(command);

        return Accepted(new
        {
            OrderId = orderId, request.Step, Message = $"Retry command sent for step: {request.Step}"
        });
    }

    /// <summary>
    ///     Get detailed saga state for debugging.
    ///     Use Case: Support engineer investigating stuck order, needs to see full saga history.
    ///     Returns: All state transitions, failed attempts, current state, correlation IDs.
    /// </summary>
    [HttpGet("{orderId:guid}/saga-details")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSagaDetails([FromRoute] Guid orderId)
    {
        // This would be implemented by querying the Wolverine saga storage
        // For now, return placeholder
        return Ok(new
        {
            OrderId = orderId, Message = "Saga details endpoint (to be implemented - query Wolverine saga storage)"
        });
    }

    /// <summary>
    ///     Bulk retry all stuck orders (dangerous operation, admin-only).
    ///     Use Case: Stripe was down for 2 hours, now 500 orders are stuck in ProcessingPayment.
    ///     Instead of manually retrying each one, bulk retry all.
    /// </summary>
    [HttpPost("bulk-retry-stuck")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> BulkRetryStuckOrders([FromBody] BulkRetryRequest request)
    {
        _logger.LogWarning(
            "BULK MANUAL INTERVENTION: Retrying {Count} stuck orders. State: {State}. User: {User}",
            request.MaxOrdersToRetry, request.SagaState, User.Identity?.Name);

        var command = new BulkRetrySagasCommand(
            request.SagaState,
            request.MaxOrdersToRetry,
            User.Identity?.Name ?? "Unknown");

        await _bus.PublishAsync(command);

        return Accepted(new
        {
            Message = $"Bulk retry initiated for {request.MaxOrdersToRetry} orders in {request.SagaState} state.",
            Warning = "Monitor metrics dashboard to ensure system stability."
        });
    }
}

// ═══════════════════════════════════════════════════════════════
// Request DTOs
// ═══════════════════════════════════════════════════════════════

public record ForceCompleteSagaRequest(
    string Reason, // "Payment verified in Stripe Dashboard"
    string? Notes = null);

public record OverridePaymentStatusRequest(
    string PaymentStatus, // "Completed", "Failed", "Refunded"
    string? StripeChargeId, // "ch_3P1..."
    string Reason);

public record ForceCancelOrderRequest(
    string Reason,
    decimal RefundAmount,
    bool NotifyCustomer = true);

public record RetryStepRequest(
    string Step); // "ProcessPayment", "ReserveInventory", "CreateShippingLabel"

public record BulkRetryRequest(
    string SagaState, // "ProcessingPayment", "ReservingInventory"
    int MaxOrdersToRetry = 100);

// ═══════════════════════════════════════════════════════════════
// Admin Commands (Handled by Wolverine)
// ═══════════════════════════════════════════════════════════════

public record ForceCompleteOrderSagaCommand(
    Guid OrderId,
    string Reason,
    string ProcessedByUserId,
    DateTimeOffset ProcessedAt);

public record OverridePaymentStatusCommand(
    Guid OrderId,
    string PaymentStatus,
    string? StripeChargeId,
    string Reason,
    string ProcessedByUserId);

public record ForceCancelOrderCommand(
    Guid OrderId,
    string Reason,
    decimal RefundAmount,
    bool NotifyCustomer,
    string ProcessedByUserId);

public record RetrySagaStepCommand(
    Guid OrderId,
    string Step,
    string ProcessedByUserId);

public record BulkRetrySagasCommand(
    string SagaState,
    int MaxOrdersToRetry,
    string ProcessedByUserId);
