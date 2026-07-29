using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Admin;

public class AdminOrderRecoveryEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/admin/orders")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Admin Order Recovery")
            .RequireAuthorization("AdminElevated")
            .RequireRateLimiting("AdminStrict");

        group.MapPost("{orderId:guid}/force-complete", ForceCompleteSaga)
            .WithName("ForceCompleteSaga")
            .WithSummary("Force-complete an order stuck in ManualInterventionRequired state")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("{orderId:guid}/override-payment-status", OverridePaymentStatus)
            .WithName("OverridePaymentStatus")
            .WithSummary("Override payment status when manually verified in Stripe")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("{orderId:guid}/force-cancel", ForceCancelOrder)
            .WithName("ForceCancelOrder")
            .WithSummary("Cancel an order stuck in intermediate state")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("{orderId:guid}/retry-step", RetryFailedStep)
            .WithName("RetryFailedStep")
            .WithSummary("Retry a failed saga step")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("{orderId:guid}/saga-details", GetSagaDetails)
            .WithName("GetSagaDetails")
            .WithSummary("Get detailed saga state for debugging")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        var bulkGroup = app.MapGroup("/api/admin/orders")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Admin Order Recovery")
            .RequireAuthorization("AdminElevated")
            .RequireRateLimiting("AdminStrict");

        bulkGroup.MapPost("bulk-retry-stuck", BulkRetryStuckOrders)
            .WithName("BulkRetryStuckOrders")
            .WithSummary("Bulk retry all stuck orders (admin-only)")
            .Produces(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ForceCompleteSaga(
        Guid orderId,
        ForceCompleteSagaRequest request,
        [FromServices] IMessageBus bus, // 👈 Explicit FromServices
        HttpContext httpContext,
        ILogger<AdminOrderRecoveryEndpoints> logger)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogWarning(
            "MANUAL INTERVENTION: Force-completing order {OrderId}. Reason: {Reason}. User: {UserId}",
            orderId, request.Reason, userName);

        var command = new ForceCompleteOrderSagaCommand(
            orderId,
            request.Reason,
            userName,
            DateTimeOffset.UtcNow);

        await bus.PublishAsync(command);

        return Results.Accepted(null, new
        {
            OrderId = orderId,
            Message = "Force-complete command sent. Saga will be marked as completed.",
            request.Reason,
            ProcessedBy = userName
        });
    }

    private static async Task<IResult> OverridePaymentStatus(
        Guid orderId,
        OverridePaymentStatusRequest request,
        [FromServices] IMessageBus bus, // 👈 Explicit FromServices
        HttpContext httpContext,
        ILogger<AdminOrderRecoveryEndpoints> logger)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogWarning(
            "MANUAL INTERVENTION: Overriding payment status for order {OrderId}. " +
            "Status: {Status}, Stripe Charge ID: {ChargeId}, Reason: {Reason}",
            orderId, request.PaymentStatus, request.StripeChargeId, request.Reason);

        var command = new OverridePaymentStatusCommand(
            orderId,
            request.PaymentStatus,
            request.StripeChargeId,
            request.Reason,
            userName);

        await bus.PublishAsync(command);

        return Results.Accepted(null, new
        {
            OrderId = orderId,
            NewStatus = request.PaymentStatus,
            ChargeId = request.StripeChargeId,
            Message = "Payment status override sent."
        });
    }

    private static async Task<IResult> ForceCancelOrder(
        Guid orderId,
        ForceCancelOrderRequest request,
        [FromServices] IMessageBus bus, // 👈 Explicit FromServices
        HttpContext httpContext,
        ILogger<AdminOrderRecoveryEndpoints> logger)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogWarning(
            "MANUAL INTERVENTION: Force-cancelling order {OrderId}. Reason: {Reason}. " +
            "Refund Amount: {RefundAmount}",
            orderId, request.Reason, request.RefundAmount);

        var command = new ForceCancelOrderCommand(
            orderId,
            request.Reason,
            request.RefundAmount,
            request.NotifyCustomer,
            userName);

        await bus.PublishAsync(command);

        return Results.Accepted(null, new
        {
            OrderId = orderId,
            request.RefundAmount,
            request.NotifyCustomer,
            Message = "Force-cancel command sent. Order will be cancelled and refunded."
        });
    }

    private static async Task<IResult> RetryFailedStep(
        Guid orderId,
        RetryStepRequest request,
        [FromServices] IMessageBus bus, // 👈 Explicit FromServices
        HttpContext httpContext,
        ILogger<AdminOrderRecoveryEndpoints> logger)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogInformation(
            "MANUAL INTERVENTION: Retrying saga step for order {OrderId}. Step: {Step}",
            orderId, request.Step);

        var command = new RetrySagaStepCommand(
            orderId,
            request.Step,
            userName);

        await bus.PublishAsync(command);

        return Results.Accepted(null, new
        {
            OrderId = orderId,
            request.Step,
            Message = $"Retry command sent for step: {request.Step}"
        });
    }

    private static Task<IResult> GetSagaDetails(Guid orderId)
    {
        return Task.FromResult(Results.Ok(new
        {
            OrderId = orderId,
            Message = "Saga details endpoint"
        }));
    }

    private static async Task<IResult> BulkRetryStuckOrders(
        BulkRetryRequest request,
        [FromServices] IMessageBus bus, // 👈 Explicit FromServices
        HttpContext httpContext,
        ILogger<AdminOrderRecoveryEndpoints> logger)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogWarning(
            "BULK MANUAL INTERVENTION: Retrying {Count} stuck orders. State: {State}. User: {User}",
            request.MaxOrdersToRetry, request.SagaState, userName);

        var command = new BulkRetrySagasCommand(
            request.SagaState,
            request.MaxOrdersToRetry,
            userName);

        await bus.PublishAsync(command);

        return Results.Accepted(null, new
        {
            Message = $"Bulk retry initiated for {request.MaxOrdersToRetry} orders in {request.SagaState} state.",
            Warning = "Monitor metrics dashboard to ensure system stability."
        });
    }
}

public record ForceCompleteSagaRequest(
    string Reason,
    string? Notes = null);

public record OverridePaymentStatusRequest(
    string PaymentStatus,
    string? StripeChargeId,
    string Reason);

public record ForceCancelOrderRequest(
    string Reason,
    decimal RefundAmount,
    bool NotifyCustomer = true);

public record RetryStepRequest(
    string Step);

public record BulkRetryRequest(
    string SagaState,
    int MaxOrdersToRetry = 100);

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
