using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Finance.Domain.Reconciliation;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Admin;

public class AdminFinanceEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/admin/finance")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Admin Finance")
            .RequireAuthorization("AdminElevated")
            .RequireRateLimiting("AdminStrict");

        group.MapGet("/reconciliation-sessions", GetReconciliationSessions)
            .WithName("GetReconciliationSessions")
            .WithSummary("Get reconciliation sessions with optional filtering")
            .Produces<IEnumerable<ReconciliationSession>>();

        group.MapGet("/reconciliation-sessions/{sessionId:guid}", GetReconciliationSession)
            .WithName("GetReconciliationSession")
            .WithSummary("Get a specific reconciliation session with details")
            .Produces<ReconciliationSession>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/reconciliation-sessions/trigger", TriggerReconciliation)
            .WithName("TriggerReconciliation")
            .WithSummary("Manually trigger reconciliation for a specific date")
            .Produces(StatusCodes.Status202Accepted);

        group.MapPost("/discrepancies/resolve", ResolveDiscrepancy)
            .WithName("ResolveDiscrepancy")
            .WithSummary("Resolve a financial discrepancy manually")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/alerts/mismatched-sessions", GetMismatchedSessions)
            .WithName("GetMismatchedSessions")
            .WithSummary("Get mismatched sessions requiring attention")
            .Produces<IEnumerable<ReconciliationSession>>();
    }

    private static async Task<IResult> GetReconciliationSessions(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] ReconciliationStatus? status,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] IReconciliationSessionRepository sessionRepo, // 👈 Explicit FromServices
        HttpContext httpContext)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 50 : pageSize;

        var sessions = await sessionRepo.GetSessionsInDateRangeAsync(
            startDate ?? DateTime.UtcNow.AddDays(-30),
            endDate ?? DateTime.UtcNow);

        var filtered = sessions.AsEnumerable();

        if (status.HasValue)
        {
            filtered = filtered.Where(s => s.Status == status.Value);
        }

        var result = filtered
            .OrderByDescending(s => s.CalculatedForDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> GetReconciliationSession(
        Guid sessionId,
        [FromServices] IReconciliationSessionRepository sessionRepo) // 👈 Explicit FromServices
    {
        var session = await sessionRepo.GetByIdAsync(sessionId);
        if (session == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(session);
    }

    private static async Task<IResult> TriggerReconciliation(
        TriggerReconciliationRequest request,
        [FromServices] IMessageBus bus, // 👈 Explicit FromServices
        HttpContext httpContext,
        ILogger<AdminFinanceEndpoints> logger)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogInformation("Manual reconciliation trigger for {Date} by {User}",
            request.Date.ToShortDateString(), userName);

        var command = new CheckDailyReconciliation(request.Date);
        await bus.PublishAsync(command);

        return Results.Accepted(null, new
        {
            Message = $"Reconciliation started for {request.Date.ToShortDateString()}",
            RequestedBy = userName,
            RequestId = Guid.NewGuid()
        });
    }

    private static async Task<IResult> ResolveDiscrepancy(
        ResolveDiscrepancyRequest request,
        [FromServices] IMessageBus bus, // 👈 Explicit FromServices
        HttpContext httpContext,
        ILogger<AdminFinanceEndpoints> logger)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogWarning(
            "MANUAL DISCREPANCY RESOLUTION: Session={SessionId}, Txn={TxnId}, Action={Action}, User={User}",
            request.SessionId, request.ExternalTxnId, request.Action, userName);

        var command = new ResolveDiscrepancyCommand(
            request.SessionId,
            request.ExternalTxnId,
            request.Action,
            request.Reason,
            userName);

        await bus.PublishAsync(command);

        return Results.Accepted(null, new
        {
            Message = $"Discrepancy resolution initiated: {request.Action}",
            SessionId = request.SessionId,
            ExternalTxnId = request.ExternalTxnId,
            ProcessedBy = userName
        });
    }

    private static async Task<IResult> GetMismatchedSessions(
        [FromQuery] DateTime? since,
        [FromServices] IReconciliationSessionRepository sessionRepo) // 👈 Explicit FromServices
    {
        var sessions = await sessionRepo.GetMismatchedSessionsAsync(
            since ?? DateTime.UtcNow.AddDays(-7));

        return Results.Ok(sessions);
    }
}

public record TriggerReconciliationRequest(DateTime Date);

public record ResolveDiscrepancyRequest(
    Guid SessionId,
    string ExternalTxnId,
    DiscrepancyResolutionAction Action,
    string Reason);
